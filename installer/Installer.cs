// 투신도시 3DS 한국어 패치 설치 프로그램 v1.1.0 (Claude, 2026-10-24; v1.1.0 은 10-25)
// 실행 코드 속 글 패치 code.ips 는 exefs\code.ips(Azahar)와 code.ips(Luma) 두 자리에 넣는다(IPS 라 두 번 적용돼도 결과가 같다).
// 사용자 원본(복호화 롬 파일 또는 romfs 폴더)에서 파일 245개를 읽어 xdelta 를 적용하고
// Azahar 모드 폴더 / 3DS SD 카드(luma\titles) / 고른 폴더에 0004000000112000\romfs 로 넣는다.
// 명령 줄(시험용): --source <롬|폴더> --target-dir <...\0004000000112000> [--log <파일>]
//                  --uninstall --target-dir <...\0004000000112000> [--log <파일>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TT3KoPatch
{
    class Entry
    {
        public string Path; public bool Gzip; public string SrcSha; public long SrcSize;
        public string OutSha; public long OutSize; public string Delta;
    }

    class PatchError : Exception { public PatchError(string m) : base(m) { } }

    interface ISource
    {
        bool TryRead(string rel, long expectSize, out byte[] data);
        string Describe { get; }
    }

    class FolderSource : ISource
    {
        string root;
        public FolderSource(string dir)
        {
            // romfs 폴더 자체, 그 위 폴더(romfs 하위), 덤프 폴더 모두 받아 준다
            string[] tries = { dir, Path.Combine(dir, "romfs") };
            foreach (string t in tries)
                if (File.Exists(Path.Combine(t, "sys_scene.bin")) && Directory.Exists(Path.Combine(t, "map"))) { root = t; return; }
            throw new PatchError("이 폴더에서 투신도시 romfs 를 찾지 못했습니다(sys_scene.bin, map 폴더가 있는 곳을 고르세요).\r\n" + dir);
        }
        public string Describe { get { return "romfs 폴더: " + root; } }
        public bool TryRead(string rel, long expectSize, out byte[] data)
        {
            string p = Path.Combine(root, rel.Replace('/', '\\'));
            data = null;
            if (!File.Exists(p)) return false;
            data = File.ReadAllBytes(p); return true;
        }
    }

    class RomSource : ISource
    {
        string file; string kind;
        Dictionary<string, long[]> index = new Dictionary<string, long[]>(StringComparer.Ordinal);

        static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        static long Align(long v, long a) { return (v + a - 1) / a * a; }
        static byte[] ReadAt(FileStream fs, long off, int len)
        {
            byte[] b = new byte[len]; fs.Seek(off, SeekOrigin.Begin);
            int got = 0; while (got < len) { int n = fs.Read(b, got, len - got); if (n <= 0) throw new PatchError("롬 파일이 중간에 끝납니다(잘린 파일?)."); got += n; }
            return b;
        }
        static string Magic(byte[] b, int o) { return Encoding.ASCII.GetString(b, o, 4); }

        public RomSource(string path)
        {
            file = path;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] h = ReadAt(fs, 0, 0x200);
                long ncch;
                if (Magic(h, 0x100) == "NCSD") { kind = ".3ds/.cci"; long mu = 0x200L << h[0x188 + 6]; ncch = (long)U32(h, 0x120) * mu; }
                else if (Magic(h, 0x100) == "NCCH") { kind = ".cxi"; ncch = 0; }
                else if (U32(h, 0) == 0x2020)
                {
                    kind = "CIA";
                    ncch = Align(U32(h, 0), 64) + Align(U32(h, 8), 64) + Align(U32(h, 0xC), 64) + Align(U32(h, 0x10), 64);
                    byte[] t = ReadAt(fs, ncch, 0x200);
                    if (Magic(t, 0x100) != "NCCH") throw new PatchError("CIA 안의 게임 데이터가 암호화되어 있어 읽을 수 없습니다.\r\n복호화된 롬을 고르세요.");
                }
                else throw new PatchError("알 수 없는 롬 형식입니다(.3ds/.cci/.cxi/.cia 를 고르세요).");
                byte[] n = ReadAt(fs, ncch, 0x200);
                if (Magic(n, 0x100) != "NCCH") throw new PatchError("롬 안에서 게임(NCCH)을 찾지 못했습니다.");
                string pid = Encoding.ASCII.GetString(n, 0x150, 16).TrimEnd('\0');
                if ((n[0x18F] & 0x4) == 0) throw new PatchError("암호화된 롬입니다(" + kind + ", " + pid + ").\r\n이 설치 프로그램은 복호화된 롬이나 romfs 폴더만 읽을 수 있습니다.\r\n자기 3DS에서 복호화해 뽑은 롬을 고르세요.");
                long nmu = 0x200L << n[0x18E];
                long romfs = ncch + (long)U32(n, 0x1B0) * nmu;
                if (U32(n, 0x1B4) == 0) throw new PatchError("이 롬에는 romfs 가 없습니다.");
                byte[] iv = ReadAt(fs, romfs, 0x60);
                if (Magic(iv, 0) != "IVFC") throw new PatchError("romfs 머리(IVFC)를 찾지 못했습니다. 암호화된 롬일 수 있습니다.");
                long l3 = romfs + Align(0x60 + U32(iv, 8), 1L << (int)U32(iv, 0x4C));
                byte[] lh = ReadAt(fs, l3, 0x28);
                byte[] dirs = ReadAt(fs, l3 + U32(lh, 0x0C), (int)U32(lh, 0x10));
                byte[] files = ReadAt(fs, l3 + U32(lh, 0x1C), (int)U32(lh, 0x20));
                long data = l3 + U32(lh, 0x24);
                Walk(dirs, files, 0, "", data);
            }
            if (index.Count == 0) throw new PatchError("romfs 에서 파일을 하나도 찾지 못했습니다.");
        }

        void Walk(byte[] dirs, byte[] files, uint d, string prefix, long data)
        {
            uint f = U32(dirs, (int)d + 0x0C);
            while (f != 0xFFFFFFFF)
            {
                int o = (int)f;
                long off = BitConverter.ToInt64(files, o + 8), size = BitConverter.ToInt64(files, o + 0x10);
                string name = Encoding.Unicode.GetString(files, o + 0x20, (int)U32(files, o + 0x1C));
                index[prefix + name] = new long[] { data + off, size };
                f = U32(files, o + 4);
            }
            uint c = U32(dirs, (int)d + 0x08);
            while (c != 0xFFFFFFFF)
            {
                int o = (int)c;
                string name = Encoding.Unicode.GetString(dirs, o + 0x18, (int)U32(dirs, o + 0x14));
                Walk(dirs, files, c, prefix + name + "/", data);
                c = U32(dirs, o + 4);
            }
        }

        public string Describe { get { return "롬 파일(" + kind + "): " + file; } }
        public bool TryRead(string rel, long expectSize, out byte[] data)
        {
            data = null; long[] e;
            if (!index.TryGetValue(rel, out e)) return false;
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                data = ReadAt(fs, e[0], (int)e[1]);
            return true;
        }
    }

    static class Core
    {
        public const string TitleId = "0004000000112000";
        public const string Marker = "tt3_korean_patch.txt";
        public const string Version = "1.1.0";
        public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        public static List<Entry> LoadManifest()
        {
            string p = Path.Combine(BaseDir, "manifest.tsv");
            if (!File.Exists(p)) throw new PatchError("manifest.tsv 가 없습니다. 압축을 모두 푼 폴더에서 실행하세요.");
            List<Entry> list = new List<Entry>();
            foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] c = line.Split('\t');
                Entry e = new Entry();
                e.Path = c[0]; e.Gzip = c[1] == "1"; e.SrcSha = c[2]; e.SrcSize = long.Parse(c[3]);
                e.OutSha = c[4]; e.OutSize = long.Parse(c[5]); e.Delta = c[6];
                list.Add(e);
            }
            return list;
        }

        public static string Sha(byte[] b)
        {
            using (SHA256 s = SHA256.Create())
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte x in s.ComputeHash(b)) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        static byte[] Gunzip(byte[] b)
        {
            using (MemoryStream i = new MemoryStream(b))
            using (GZipStream g = new GZipStream(i, CompressionMode.Decompress))
            using (MemoryStream o = new MemoryStream()) { g.CopyTo(o); return o.ToArray(); }
        }

        static byte[] Gzip(byte[] b)
        {
            using (MemoryStream o = new MemoryStream())
            {
                using (GZipStream g = new GZipStream(o, CompressionLevel.Optimal, true)) g.Write(b, 0, b.Length);
                return o.ToArray();
            }
        }

        static void RunXdelta(string src, string delta, string outp)
        {
            string exe = Path.Combine(BaseDir, "tools", "xdelta3.exe");
            if (!File.Exists(exe)) throw new PatchError("tools\\xdelta3.exe 가 없습니다. 압축을 모두 푼 폴더에서 실행하세요.");
            // -n: 검사합 확인 안 함, -a: 원본 BLAKE3 확인 안 함(원본이 달라도 적용 — 사용자 지시 10-24 「해시에 상관없이 패치가 되게」)
            ProcessStartInfo si = new ProcessStartInfo(exe, "-d -f -n -a -s \"" + src + "\" \"" + delta + "\" \"" + outp + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string err = p.StandardError.ReadToEnd(); p.WaitForExit();
                if (p.ExitCode != 0) throw new PatchError("xdelta 적용 실패: " + Path.GetFileName(delta) + "\r\n" + err);
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandleW(IntPtr h, StringBuilder buf, uint len, uint flags);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        // 정션·심볼릭 링크를 실제 경로로 푼다(예: %APPDATA%\Azahar 가 다른 드라이브로 가는 정션이면
        // 그 아래에서 폴더 이름 바꾸기가 「경로를 찾을 수 없음」으로 실패한다 — 10-24 이 PC에서 겪음).
        public static string Resolve(string path)
        {
            string full = Path.GetFullPath(path), tail = "", cur = full;
            while (cur != null && !Directory.Exists(cur))
            {
                tail = Path.Combine(Path.GetFileName(cur), tail);
                cur = Path.GetDirectoryName(cur);
            }
            if (cur == null) return full;
            IntPtr h = CreateFileW(cur, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero); // FILE_FLAG_BACKUP_SEMANTICS
            if (h == new IntPtr(-1)) return full;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n == 0 || n >= sb.Capacity) return full;
                string real = sb.ToString();
                if (real.StartsWith(@"\\?\UNC\")) real = @"\\" + real.Substring(8);
                else if (real.StartsWith(@"\\?\")) real = real.Substring(4);
                return tail.Length > 0 ? Path.Combine(real, tail.TrimEnd('\\')) : real;
            }
            finally { CloseHandle(h); }
        }

        public static void Install(ISource src, string tidDir, Action<string> log, Action<int, int> progress)
        {
            tidDir = Resolve(tidDir);
            List<Entry> list = LoadManifest();
            log("원본: " + src.Describe);
            log("설치할 곳: " + tidDir);
            // 원본 해시는 확인만 하고 막지 않는다(사용자 지시 10-24 「해시에 상관없이 패치가 되게」).
            // 파일이 아예 없거나 적용 중 오류가 난 파일만 건너뛰고 끝에 알린다.
            int i = 0, differ = 0, outDiffer = 0, done = 0;
            List<string> skipped = new List<string>();
            string staging = tidDir + ".installing";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            string tmp = Path.Combine(Path.GetTempPath(), "tt3ko_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string s = Path.Combine(tmp, "src.bin"), o = Path.Combine(tmp, "out.bin");
                foreach (Entry e in list)
                {
                    progress(++i, list.Count);
                    byte[] d;
                    if (!src.TryRead(e.Path, e.SrcSize, out d)) { skipped.Add(e.Path + " (원본에 없음)"); continue; }
                    if (d.Length != e.SrcSize || Sha(d) != e.SrcSha) { differ++; if (differ <= 5) log("  원본이 기대와 다름(그래도 적용): " + e.Path); }
                    try
                    {
                        bool gz = e.Gzip && d.Length > 2 && d[0] == 0x1f && d[1] == 0x8b;
                        File.WriteAllBytes(s, gz ? Gunzip(d) : d);
                        if (File.Exists(o)) File.Delete(o);
                        RunXdelta(s, Path.Combine(BaseDir, "patch", e.Delta.Replace('/', '\\')), o);
                        byte[] r = File.ReadAllBytes(o);
                        if (r.Length != e.OutSize || Sha(r) != e.OutSha) outDiffer++;
                        byte[] w = e.Gzip ? Gzip(r) : r;
                        if (e.Gzip && Sha(Gunzip(w)) != Sha(r)) throw new PatchError("압축 확인 실패");
                        string dst = Path.Combine(staging, "romfs", e.Path.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.WriteAllBytes(dst, w);
                        done++;
                    }
                    catch (Exception ex) { skipped.Add(e.Path + " (" + (ex is PatchError ? ex.Message.Split('\r')[0] : ex.GetType().Name) + ")"); }
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
            if (done == 0) { try { Directory.Delete(staging, true); } catch { } throw new PatchError("파일을 하나도 패치하지 못했습니다. 원본(롬이나 romfs 폴더)을 확인하세요."); }
            // 실행 코드 속 글(능력치 이름·레벨업 문구 등). 원본이 필요 없는 IPS 라 그대로 복사한다.
            string ips = Path.Combine(BaseDir, "patch", "code.ips");
            bool codeDone = false;
            if (File.Exists(ips))
            {
                Directory.CreateDirectory(Path.Combine(staging, "exefs"));
                File.Copy(ips, Path.Combine(staging, "exefs", "code.ips"), true);
                File.Copy(ips, Path.Combine(staging, "code.ips"), true);
                codeDone = true;
                log("실행 코드 패치(code.ips)를 넣었습니다.");
            }
            else log("patch\\code.ips 가 없어 실행 코드 패치는 건너뜁니다.");
            if (differ > 0) log("원본이 기대와 다른 파일: " + differ + "개(그래도 적용함)");
            if (outDiffer > 0) log("결과가 기대와 다른 파일: " + outDiffer + "개(원본이 다르면 생길 수 있음)");
            foreach (string sk in skipped) log("  건너뜀: " + sk);
            File.WriteAllText(Path.Combine(staging, Marker),
                "투신도시 3DS 한국어 패치 v" + Version + "\r\n설치: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n파일: " + done + "/" + list.Count +
                "\r\n원본이 기대와 다른 파일: " + differ + "\r\n결과가 기대와 다른 파일: " + outDiffer + "\r\n건너뛴 파일: " + skipped.Count + "\r\n실행 코드 패치: " + (codeDone ? "넣음" : "없음") + "\r\n", Encoding.UTF8);
            // 3) 바꿔 넣기 (원래 있던 폴더는 백업)
            if (Directory.Exists(tidDir))
            {
                string bak = tidDir + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(tidDir, bak);
                log("원래 있던 폴더를 백업했습니다: " + Path.GetFileName(bak));
            }
            Directory.Move(staging, tidDir);
            if (differ == 0 && outDiffer == 0 && skipped.Count == 0) log("설치 완료: 파일 " + done + "개, 원본·결과 모두 일치");
            else log("설치 완료: 파일 " + done + "/" + list.Count + "개 (위 경고 참고)");
        }

        public static void Uninstall(string tidDir, Action<string> log)
        {
            tidDir = Resolve(tidDir);
            if (!Directory.Exists(tidDir)) throw new PatchError("설치된 패치가 없습니다: " + tidDir);
            if (!File.Exists(Path.Combine(tidDir, Marker))) throw new PatchError("이 폴더는 이 설치 프로그램이 넣은 것이 아니라 지우지 않았습니다:\r\n" + tidDir);
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(tidDir,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            log("패치 폴더를 휴지통으로 옮겼습니다.");
            string parent = Path.GetDirectoryName(tidDir); string latest = null;
            foreach (string d in Directory.GetDirectories(parent, TitleId + ".backup-*"))
                if (latest == null || string.CompareOrdinal(d, latest) > 0) latest = d;
            if (latest != null) { Directory.Move(latest, tidDir); log("백업을 되돌렸습니다: " + Path.GetFileName(latest)); }
        }
    }

    class MainForm : Form
    {
        RadioButton srcRom, srcDir, dstEmu, dstSd, dstDir;
        TextBox srcPath, dirPath, logBox;
        ComboBox emuBox, sdBox;
        Button install, uninstall;
        ProgressBar bar;

        public MainForm()
        {
            Text = "투신도시 3DS 한국어 패치 v" + Core.Version + " 설치";
            Font = new Font("맑은 고딕", 9f);
            ClientSize = new Size(640, 560); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Label intro = new Label();
            intro.Text = "일본판 「闘神都市」(3DS) 한국어 패치입니다. 롬은 고치지 않고, 모드 폴더(LayeredFS)에 파일을 넣습니다.\r\n" +
                         "원본은 복호화된 롬 파일이나 romfs 폴더가 필요합니다(암호화된 롬은 읽을 수 없습니다).";
            intro.SetBounds(12, 10, 616, 40); Controls.Add(intro);

            GroupBox g1 = new GroupBox(); g1.Text = "1. 원본"; g1.SetBounds(12, 54, 616, 86); Controls.Add(g1);
            srcRom = new RadioButton(); srcRom.Text = "롬 파일 (.3ds .cci .cxi .cia)"; srcRom.Checked = true; srcRom.SetBounds(12, 20, 220, 22); g1.Controls.Add(srcRom);
            srcDir = new RadioButton(); srcDir.Text = "romfs 폴더 (Azahar 「Dump RomFS」 등)"; srcDir.SetBounds(240, 20, 300, 22); g1.Controls.Add(srcDir);
            srcPath = new TextBox(); srcPath.SetBounds(12, 50, 500, 23); g1.Controls.Add(srcPath);
            Button b1 = new Button(); b1.Text = "찾아보기…"; b1.SetBounds(518, 49, 86, 25); b1.Click += PickSource; g1.Controls.Add(b1);

            GroupBox g2 = new GroupBox(); g2.Text = "2. 설치할 곳"; g2.SetBounds(12, 146, 616, 124); Controls.Add(g2);
            dstEmu = new RadioButton(); dstEmu.Text = "Azahar"; dstEmu.Checked = true; dstEmu.SetBounds(12, 22, 124, 22); g2.Controls.Add(dstEmu);
            emuBox = new ComboBox(); emuBox.DropDownStyle = ComboBoxStyle.DropDownList; emuBox.SetBounds(140, 21, 372, 23); g2.Controls.Add(emuBox);
            Button b2 = new Button(); b2.Text = "직접 고르기…"; b2.SetBounds(518, 20, 86, 25); b2.Click += PickEmu; g2.Controls.Add(b2);
            dstSd = new RadioButton(); dstSd.Text = "3DS SD 카드"; dstSd.SetBounds(12, 54, 124, 22); g2.Controls.Add(dstSd);
            sdBox = new ComboBox(); sdBox.DropDownStyle = ComboBoxStyle.DropDownList; sdBox.SetBounds(140, 53, 372, 23); g2.Controls.Add(sdBox);
            Button b3 = new Button(); b3.Text = "새로 찾기"; b3.SetBounds(518, 52, 86, 25); b3.Click += delegate { FillSd(); }; g2.Controls.Add(b3);
            dstDir = new RadioButton(); dstDir.Text = "폴더로 내보내기"; dstDir.SetBounds(12, 86, 124, 22); g2.Controls.Add(dstDir);
            dirPath = new TextBox(); dirPath.SetBounds(140, 85, 372, 23); g2.Controls.Add(dirPath);
            Button b4 = new Button(); b4.Text = "찾아보기…"; b4.SetBounds(518, 84, 86, 25); b4.Click += PickOut; g2.Controls.Add(b4);

            install = new Button(); install.Text = "설치"; install.SetBounds(12, 280, 120, 32); install.Click += DoInstall; Controls.Add(install);
            uninstall = new Button(); uninstall.Text = "제거"; uninstall.SetBounds(140, 280, 120, 32); uninstall.Click += DoUninstall; Controls.Add(uninstall);
            bar = new ProgressBar(); bar.SetBounds(270, 284, 358, 24); Controls.Add(bar);
            logBox = new TextBox(); logBox.Multiline = true; logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical;
            logBox.SetBounds(12, 322, 616, 226); Controls.Add(logBox);
            FillEmu(); FillSd();
            Log("SD 카드에 넣은 뒤에는 Luma 설정(SELECT 누른 채 전원)에서 「Enable game patching」을 켜야 합니다.");
        }

        void Log(string s) { logBox.AppendText(s + "\r\n"); }

        void FillEmu()
        {
            emuBox.Items.Clear();
            string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (string n in new string[] { "Azahar", "Lime3DS", "Citra" })
            {
                string p = Path.Combine(ad, n);
                if (Directory.Exists(p)) emuBox.Items.Add(p);
            }
            if (emuBox.Items.Count == 0) emuBox.Items.Add(Path.Combine(ad, "Azahar"));
            emuBox.SelectedIndex = 0;
        }

        void FillSd()
        {
            sdBox.Items.Clear();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Removable || !d.IsReady) continue;
                    bool hint = Directory.Exists(Path.Combine(d.RootDirectory.FullName, "Nintendo 3DS")) || Directory.Exists(Path.Combine(d.RootDirectory.FullName, "luma"));
                    sdBox.Items.Add(d.RootDirectory.FullName + (hint ? "" : "  (3DS 폴더 없음)"));
                }
                catch { }
            }
            if (sdBox.Items.Count > 0) sdBox.SelectedIndex = 0;
        }

        void PickSource(object o, EventArgs e)
        {
            if (srcRom.Checked)
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = "3DS 롬 (*.3ds;*.cci;*.cxi;*.cia)|*.3ds;*.cci;*.cxi;*.cia|모든 파일|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) srcPath.Text = d.FileName;
            }
            else
            {
                FolderBrowserDialog d = new FolderBrowserDialog(); d.Description = "romfs 폴더를 고르세요(sys_scene.bin 이 있는 곳)";
                if (d.ShowDialog(this) == DialogResult.OK) srcPath.Text = d.SelectedPath;
            }
        }

        void PickEmu(object o, EventArgs e)
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "Azahar 사용자 폴더(load·sdmc 가 있는 곳)나, 포터블판이면 azahar.exe 가 있는 폴더를 고르세요";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string p = d.SelectedPath;
            if (Directory.Exists(Path.Combine(p, "user"))) p = Path.Combine(p, "user");
            emuBox.Items.Insert(0, p); emuBox.SelectedIndex = 0; dstEmu.Checked = true;
        }

        void PickOut(object o, EventArgs e)
        {
            FolderBrowserDialog d = new FolderBrowserDialog(); d.Description = "패치 폴더(0004000000112000)를 만들 곳을 고르세요";
            if (d.ShowDialog(this) == DialogResult.OK) { dirPath.Text = d.SelectedPath; dstDir.Checked = true; }
        }

        string TargetDir()
        {
            if (dstEmu.Checked)
            {
                if (emuBox.SelectedItem == null) throw new PatchError("Azahar 폴더를 고르세요.");
                return Path.Combine((string)emuBox.SelectedItem, "load", "mods", Core.TitleId);
            }
            if (dstSd.Checked)
            {
                if (sdBox.SelectedItem == null) throw new PatchError("SD 카드를 찾지 못했습니다. 꽂은 뒤 「새로 찾기」를 누르세요.");
                string root = ((string)sdBox.SelectedItem).Split(' ')[0];
                return Path.Combine(root, "luma", "titles", Core.TitleId);
            }
            if (dirPath.Text.Trim().Length == 0) throw new PatchError("내보낼 폴더를 고르세요.");
            return Path.Combine(dirPath.Text.Trim(), Core.TitleId);
        }

        void Busy(bool b) { install.Enabled = !b; uninstall.Enabled = !b; UseWaitCursor = b; }

        void DoInstall(object o, EventArgs ev)
        {
            string target, source = srcPath.Text.Trim();
            try
            {
                if (source.Length == 0) throw new PatchError("원본(롬 파일이나 romfs 폴더)을 고르세요.");
                target = TargetDir();
            }
            catch (PatchError e) { MessageBox.Show(this, e.Message, Text); return; }
            bool isDir = srcDir.Checked;
            Busy(true); Log("— 설치 시작 —");
            Thread t = new Thread(delegate ()
            {
                try
                {
                    ISource src = isDir ? (ISource)new FolderSource(source) : new RomSource(source);
                    Core.Install(src, target,
                        delegate (string s) { BeginInvoke((Action)delegate { Log(s); }); },
                        delegate (int a, int b) { BeginInvoke((Action)delegate { bar.Maximum = b; bar.Value = Math.Min(a, b); }); });
                    BeginInvoke((Action)delegate { MessageBox.Show(this, "설치했습니다.\r\n" + target, Text); });
                }
                catch (Exception e)
                {
                    string m = e is PatchError ? e.Message : e.ToString();
                    BeginInvoke((Action)delegate { Log("실패: " + m); MessageBox.Show(this, m, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); });
                }
                finally { BeginInvoke((Action)delegate { Busy(false); }); }
            });
            t.IsBackground = true; t.Start();
        }

        void DoUninstall(object o, EventArgs ev)
        {
            try
            {
                string target = TargetDir();
                if (MessageBox.Show(this, "이 폴더의 패치를 지웁니다(휴지통으로). 백업이 있으면 되돌립니다.\r\n" + target, Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                Core.Uninstall(target, Log);
            }
            catch (Exception e) { string m = e is PatchError ? e.Message : e.ToString(); Log("실패: " + m); MessageBox.Show(this, m, Text); }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0) return Cli(args);
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
            return 0;
        }

        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        static int Cli(string[] a)
        {
            string logPath = Arg(a, "--log");
            StreamWriter w = logPath != null ? new StreamWriter(logPath, false, new UTF8Encoding(false)) : null;
            Action<string> log = delegate (string s) { if (w != null) { w.WriteLine(s); w.Flush(); } };
            try
            {
                string target = Arg(a, "--target-dir");
                if (target == null) throw new PatchError("--target-dir 필요");
                if (Array.IndexOf(a, "--uninstall") >= 0) { Core.Uninstall(target, log); return 0; }
                string source = Arg(a, "--source");
                if (source == null) throw new PatchError("--source 필요");
                ISource src = Directory.Exists(source) ? (ISource)new FolderSource(source) : new RomSource(source);
                Core.Install(src, target, log, delegate (int x, int y) { });
                return 0;
            }
            catch (Exception e) { log("실패: " + (e is PatchError ? e.Message : e.ToString())); return 1; }
            finally { if (w != null) w.Close(); }
        }
    }
}
