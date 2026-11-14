using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LastLight.EditorTools
{
    /// <summary>
    /// Headset helpers: check that a PICO headset is connected over USB, and build a release APK and install
    /// it on the headset. Each step's outcome is written to Logs/LastLightDevice/*.txt as well as the Console.
    /// The version comes from the newest "## x.y.z" heading in CHANGELOG.md, and the version code goes up by one.
    /// </summary>
    public static class HeadsetInstaller
    {
        const string PackageName = "com.lastlight.prototype";

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string LogDir => Path.Combine(ProjectRoot, "Logs", "LastLightDevice");

        [MenuItem("Last Light/Headset/Check Connected Headset")]
        public static void CheckMenu()
        {
            var log = new StringBuilder();
            Check(log);
            Write("check.txt", log);
        }

        const string PendingKey = "LastLight.PendingBuildInstall";

        /// <summary>
        /// The PICO SDK only adds its VR manifest entries (pvr.app.type = vr and friends) when its editor code is
        /// compiled for Android. Building straight after switching platform skips that, and the headset then opens
        /// the game as a flat 2D window. So the menu switches platform first, lets the scripts recompile, and only
        /// then builds, picking up again automatically after the reload.
        /// </summary>
        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            EditorApplication.delayCall += BuildAndInstallMenu;
        }

        [MenuItem("Last Light/Headset/Build Release APK and Install")]
        public static void BuildAndInstallMenu()
        {
            var log = new StringBuilder();
            Write("install.txt", log.AppendLine("STARTED " + System.DateTime.Now.ToString("s")));
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                SessionState.SetBool(PendingKey, true);
                Write("install.txt", log.AppendLine("SWITCHING to Android; the build starts by itself once the scripts have recompiled."));
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                return;
            }
            try
            {
                // The build takes a few minutes, which leaves time to accept the headset's USB debugging prompt.
                if (!Check(log)) log.AppendLine("No headset ready yet; building anyway and waiting for it afterwards.");

                string version = ChangelogVersion() ?? PlayerSettings.bundleVersion;
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = PlayerSettings.Android.bundleVersionCode + 1;
                AssetDatabase.SaveAssets();
                log.AppendLine($"Version {version}, version code {PlayerSettings.Android.bundleVersionCode}");
                Write("install.txt", log.AppendLine("BUILDING release arm64..."));

                var t0 = System.DateTime.Now;
                bool built = PrototypeBuilder.BuildAndroid("arm64", true);
                string apk = Path.Combine(ProjectRoot, "Builds", $"LastLight-{version}.apk");
                log.AppendLine($"Build {(built ? "succeeded" : "FAILED")} in {(System.DateTime.Now - t0).TotalMinutes:0.0} min: {apk}");
                if (!built || !File.Exists(apk)) { log.AppendLine("RESULT: FAILED, build did not produce an APK"); return; }
                log.AppendLine($"APK size {new FileInfo(apk).Length / (1024f * 1024f):0.0} MB");
                if (!ManifestIsVr(log)) { log.AppendLine("RESULT: FAILED, the APK is not marked as a VR app and would open as a 2D window"); return; }
                if (!WaitForHeadset(log, 180)) { log.AppendLine("RESULT: BUILT but not installed, no authorized headset within 3 minutes"); return; }
                Write("install.txt", log.AppendLine("INSTALLING..."));

                var install = Adb($"install -r \"{apk}\"", 300000);
                log.AppendLine("adb install: " + install.Trim());
                if (!install.Contains("Success")) { log.AppendLine("RESULT: FAILED, install was not accepted"); return; }

                string installed = Adb($"shell dumpsys package {PackageName}", 20000);
                var m = Regex.Match(installed, @"versionName=(\S+)");
                var c = Regex.Match(installed, @"versionCode=(\d+)");
                log.AppendLine($"On headset: versionName {(m.Success ? m.Groups[1].Value : "?")}, versionCode {(c.Success ? c.Groups[1].Value : "?")}");

                log.AppendLine("launch: " + Adb($"shell monkey -p {PackageName} -c android.intent.category.LAUNCHER 1", 20000).Trim());
                log.AppendLine("RESULT: OK");
            }
            catch (System.Exception e)
            {
                log.AppendLine("RESULT: FAILED with exception " + e);
            }
            finally
            {
                log.AppendLine("FINISHED " + System.DateTime.Now.ToString("s"));
                Write("install.txt", log);
            }
        }

        /// <summary>
        /// Installs the newest release APK from Builds/, uninstalling the existing app first. Needed when the copy
        /// on the headset was signed with a different key; this deletes the app's saved data on the headset.
        /// </summary>
        [MenuItem("Last Light/Headset/Replace Installed App With Latest Build")]
        public static void ReplaceMenu()
        {
            var log = new StringBuilder();
            try
            {
                if (!Check(log)) { log.AppendLine("RESULT: FAILED, no headset ready"); return; }
                var apks = new DirectoryInfo(Path.Combine(ProjectRoot, "Builds")).GetFiles("LastLight-*.apk");
                if (apks.Length == 0) { log.AppendLine("RESULT: FAILED, no release APK in Builds/"); return; }
                System.Array.Sort(apks, (x, y) => y.LastWriteTimeUtc.CompareTo(x.LastWriteTimeUtc));
                string apk = apks[0].FullName;
                log.AppendLine("APK: " + apk);
                log.AppendLine("adb uninstall: " + Adb($"uninstall {PackageName}", 60000).Trim());
                string install = Adb($"install \"{apk}\"", 300000);
                log.AppendLine("adb install: " + install.Trim());
                if (!install.Contains("Success")) { log.AppendLine("RESULT: FAILED, install was not accepted"); return; }
                string installed = Adb($"shell dumpsys package {PackageName}", 20000);
                var m = Regex.Match(installed, @"versionName=(\S+)");
                var c = Regex.Match(installed, @"versionCode=(\d+)");
                log.AppendLine($"On headset: versionName {(m.Success ? m.Groups[1].Value : "?")}, versionCode {(c.Success ? c.Groups[1].Value : "?")}");
                log.AppendLine("launch: " + Adb($"shell monkey -p {PackageName} -c android.intent.category.LAUNCHER 1", 20000).Trim());
                System.Threading.Thread.Sleep(4000);
                string pid = Adb($"shell pidof {PackageName}", 10000).Trim();
                log.AppendLine("Running: " + (pid.Length > 0 ? "yes, pid " + pid : "no"));
                log.AppendLine("RESULT: OK");
            }
            catch (System.Exception e) { log.AppendLine("RESULT: FAILED with exception " + e); }
            finally { Write("replace.txt", log); }
        }

        /// <summary>Checks the generated Gradle manifest for the PICO VR entries.</summary>
        static bool ManifestIsVr(StringBuilder log)
        {
            string manifest = Path.Combine(ProjectRoot, "Library", "Bee", "Android", "Prj", "IL2CPP", "Gradle", "unityLibrary", "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { log.AppendLine("Manifest not found at " + manifest); return false; }
            string text = File.ReadAllText(manifest);
            bool vr = text.Contains("pvr.app.type") && text.Contains("\"vr\"");
            log.AppendLine("Manifest marks the app as VR: " + (vr ? "yes" : "NO"));
            return vr;
        }

        static bool WaitForHeadset(StringBuilder log, int seconds)
        {
            for (int i = 0; i <= seconds; i += 5)
            {
                string devices = Adb("devices", 10000);
                if (Regex.IsMatch(devices, @"^\S+\s+device\b", RegexOptions.Multiline))
                {
                    log.AppendLine($"Headset authorized after {i} s of waiting");
                    return true;
                }
                if (i == 0) Write("install.txt", log.AppendLine("WAITING for an authorized headset: " + devices.Replace("List of devices attached", "").Trim()));
                System.Threading.Thread.Sleep(5000);
            }
            return false;
        }

        /// <summary>Lists adb devices and the first device's model and system build. Returns true if one is ready.</summary>
        static bool Check(StringBuilder log)
        {
            string adb = AdbPath();
            log.AppendLine("adb: " + adb + (File.Exists(adb) ? "" : "  (NOT FOUND)"));
            if (!File.Exists(adb)) return false;
            string devices = Adb("devices -l", 20000);
            log.AppendLine(devices.Trim());
            bool ready = Regex.IsMatch(devices, @"^\S+\s+device\b", RegexOptions.Multiline);
            bool unauthorized = devices.Contains("unauthorized");
            if (unauthorized) log.AppendLine("A device is connected but not authorized: accept the USB debugging prompt inside the headset.");
            if (!ready) { log.AppendLine("HEADSET: none ready"); return false; }
            log.AppendLine("Model: " + Adb("shell getprop ro.product.model", 10000).Trim());
            log.AppendLine("Manufacturer: " + Adb("shell getprop ro.product.manufacturer", 10000).Trim());
            log.AppendLine("System: " + Adb("shell getprop ro.build.display.id", 10000).Trim());
            log.AppendLine("Android: " + Adb("shell getprop ro.build.version.release", 10000).Trim());
            log.AppendLine("Battery: " + Regex.Match(Adb("shell dumpsys battery", 10000), @"level: \d+").Value);
            string pkg = Adb($"shell dumpsys package {PackageName}", 20000);
            var v = Regex.Match(pkg, @"versionName=(\S+)");
            log.AppendLine("Last Light installed: " + (v.Success ? v.Groups[1].Value : "no"));
            log.AppendLine("HEADSET: ready");
            return true;
        }

        static string ChangelogVersion()
        {
            string path = Path.Combine(ProjectRoot, "CHANGELOG.md");
            if (!File.Exists(path)) return null;
            var m = Regex.Match(File.ReadAllText(path), @"^## (\d+\.\d+\.\d+)", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>The adb that ships with Unity's Android module, or the SDK set in Preferences.</summary>
        static string AdbPath()
        {
            string sdk = EditorPrefs.GetString("AndroidSdkRoot");
            if (!string.IsNullOrEmpty(sdk) && File.Exists(Path.Combine(sdk, "platform-tools", "adb"))) return Path.Combine(sdk, "platform-tools", "adb");
            // Contents -> Unity.app -> editor folder, which holds PlaybackEngines.
            string editorDir = Path.GetDirectoryName(Path.GetDirectoryName(EditorApplication.applicationContentsPath));
            return Path.Combine(editorDir, "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb");
        }

        static string Adb(string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(AdbPath(), args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } output += " (timed out)"; }
                return output;
            }
        }

        static void Write(string file, StringBuilder log)
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, file), log.ToString());
            Debug.Log("[LastLight] " + file + ":\n" + log);
        }
    }
}
