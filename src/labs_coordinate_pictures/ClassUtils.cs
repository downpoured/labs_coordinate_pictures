﻿// Copyright (c) Ben Fisher, 2016.
// Licensed under GPLv3. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace labs_coordinate_pictures
{
    public static class Utils
    {
        static Random random = new Random();

        public static void OpenDirInExplorer(string sDir)
        {
            Process.Start("explorer.exe", "\"" + sDir + "\"");
        }

        public static void SelectFileInExplorer(string path)
        {
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        // returns exit code.
        public static int Run(string executable, string[] args, bool shellExecute, bool waitForExit,
            bool hideWindow)
        {
            string stdout, stderr;
            return Run(executable, args, shellExecute, waitForExit,
                hideWindow, false, out stdout, out stderr, null);
        }

        // returns exit code. reading stdout implies waiting for exit.
        public static int Run(string executable, string[] args, bool shellExecute, bool waitForExit,
            bool hideWindow, out string stdout, out string stderr)
        {
            return Run(executable, args, shellExecute, waitForExit,
                hideWindow, true, out stdout, out stderr, null);
        }

        // returns exit code. reading stdout implies waiting for exit.
        public static int Run(string executable, string[] args, bool shellExecute, bool waitForExit,
            bool hideWindow, bool getStdout, out string outStdout,
            out string outStderr, string workingDir)
        {
            var startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = hideWindow;
            startInfo.UseShellExecute = shellExecute;
            startInfo.FileName = executable;
            startInfo.Arguments = CombineProcessArguments(args);
            startInfo.WorkingDirectory = workingDir;
            if (getStdout)
            {
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardOutput = true;
                waitForExit = true;
            }

            string stderr = "", stdout = "";
            var process = Process.Start(startInfo);
            if (getStdout)
            {
                process.OutputDataReceived += (sender, dataReceived) => stdout += dataReceived.Data;
                process.ErrorDataReceived += (sender, dataReceived) => stderr += dataReceived.Data;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            if (waitForExit)
            {
                process.WaitForExit();
            }

            outStdout = stdout;
            outStderr = stderr;

            return waitForExit ? process.ExitCode : 0;
        }

        public static bool AskToConfirm(string message)
        {
            var result = System.Windows.Forms.MessageBox.Show(message, "", MessageBoxButtons.YesNo);
            return result == DialogResult.Yes;
        }

        public static bool IsDigits(string s)
        {
            if (s == null || s.Length == 0)
            {
                return false;
            }

            foreach (var c in s)
            {
                if (!"0123456789".Contains(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static string FirstTwoChars(string s)
        {
            return s.Substring(0, Math.Min(2, s.Length));
        }

        // "soft delete" just means moving to a designated 'trash' location.
        public static string GetSoftDeleteDestination(string path)
        {
            var trashDir = Configs.Current.Get(ConfigKey.FilepathTrash);
            if (string.IsNullOrEmpty(trashDir) ||
                !Directory.Exists(trashDir))
            {
                MessageBox("Trash directory not found. Go to the main screen and to the " +
                    "option menu and click Options->Set trash directory...");
                return null;
            }

            // as a prefix, the first 2 chars of the parent directory
            var prefix = FirstTwoChars(Path.GetFileName(Path.GetDirectoryName(path))) + "_";
            return Path.Combine(trashDir, prefix + Path.GetFileName(path) + GetRandomDigits());
        }

        public static string GetRandomDigits()
        {
            return random.Next().ToString();
        }

        public static void SoftDelete(string path)
        {
            var newPath = GetSoftDeleteDestination(path);
            if (newPath != null)
            {
                SimpleLog.Current.WriteLog("Moving (" + path + ") to (" + newPath + ")");
                File.Move(path, newPath);
            }
        }

        public static string CombineProcessArguments(string[] args)
        {
            // By Roger Knapp
            // http://csharptest.net/529/how-to-correctly-escape-command-line-arguments-in-c/
            if (args == null || args.Length == 0)
            {
                return "";
            }

            StringBuilder arguments = new StringBuilder();

            // these can not be escaped
            Regex invalidChar = new Regex("[\x00\x0a\x0d]");

            // contains whitespace or two quote characters
            Regex needsQuotes = new Regex(@"\s|""");

            // one or more '\' followed with a quote or end of string
            Regex escapeQuote = new Regex(@"(\\*)(""|$)");

            for (int carg = 0; carg < args.Length; carg++)
            {
                if (invalidChar.IsMatch(args[carg]))
                {
                    throw new ArgumentOutOfRangeException("invalid character (" + carg + ")");
                }

                if (string.IsNullOrEmpty(args[carg]))
                {
                    arguments.Append("\"\"");
                }
                else if (!needsQuotes.IsMatch(args[carg]))
                {
                    arguments.Append(args[carg]);
                }
                else
                {
                    arguments.Append('"');
                    arguments.Append(escapeQuote.Replace(args[carg], m =>
                        m.Groups[1].Value + m.Groups[1].Value +
                        (m.Groups[2].Value == "\"" ? "\\\"" : "")));
                    arguments.Append('"');
                }

                if (carg + 1 < args.Length)
                {
                    arguments.Append(' ');
                }
            }

            return arguments.ToString();
        }

        public static bool RepeatWhileFileLocked(string filepath, int timeout)
        {
            int millisecondsBeforeRetry = 250;
            for (int i = 0; i < timeout; i += millisecondsBeforeRetry)
            {
                if (!IsFileLocked(filepath))
                    return true;

                Thread.Sleep(millisecondsBeforeRetry);
            }

            return false;
        }

        public static bool IsFileLocked(string filepath)
        {
            FileInfo file = new FileInfo(filepath);
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            return false;
        }

        // pretty-print a filesize as "1.24Mb" or "32k".
        public static string FormatFilesize(string filepath)
        {
            if (!File.Exists(filepath))
                return " file not found";

            var len = new FileInfo(filepath).Length;
            return (len > 1024 * 1024) ?
                string.Format(" ({0:0.00}mb)", len / (1024.0 * 1024.0)) :
                string.Format(" ({0}k)", len / 1024);
        }

        public static void CloseOtherProcessesByName(string processName)
        {
            var thisId = Process.GetCurrentProcess().Id;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (process.Id != thisId)
                    process.Kill();
            }
        }

        public static void RunPythonScriptOnSeparateThread(string pyScript,
            string[] listArgs, bool createWindow = false)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                RunPythonScript(pyScript, listArgs, createWindow: createWindow);
            });
        }

        public static string RunPythonScript(string pyScript,
            string[] listArgs, bool createWindow = false,
            bool warnIfStdErr = true, string workingDir = null)
        {
            if (!pyScript.Contains("/") && !pyScript.Contains("\\"))
            {
                pyScript = Path.Combine(Configs.Current.Directory, pyScript);
            }

            if (!File.Exists(pyScript))
            {
                MessageBox("Script not found " + pyScript);
                return "Script not found";
            }

            var python = Configs.Current.Get(ConfigKey.FilepathPython);
            if (string.IsNullOrEmpty(python) || !File.Exists(python))
            {
                MessageBox("Python exe not found. Go to the main screen and to the " +
                    "option menu and click Options->Set python location...");
                return "Python exe not found.";
            }

            var args = new List<string> { pyScript };
            args.AddRange(listArgs);
            string stdout, stderr;
            int exitCode = Run(python, args.ToArray(), shellExecute: false,
                waitForExit: true, hideWindow: !createWindow, getStdout: true,
                outStdout: out stdout, outStderr: out stderr, workingDir: workingDir);

            if (warnIfStdErr && exitCode != 0)
            {
                MessageBox("warning, error from script: " + stderr ?? "");
            }

            return stderr;
        }

        public static void RunImageConversion(string pathInput, string pathOutput,
            string resizeSpec, int jpgQuality)
        {
            if (File.Exists(pathOutput))
            {
                MessageBox("File already exists, " + pathOutput);
                return;
            }

            // send the working directory for the script so that it can find options.ini
            var workingDir = Path.Combine(Configs.Current.Directory,
                "ben_python_img");
            var script = Path.Combine(Configs.Current.Directory,
                "ben_python_img", "img_convert_resize.py");
            var args = new string[] { "convert_resize",
                pathInput, pathOutput, resizeSpec, jpgQuality.ToString() };
            var stderr = RunPythonScript(script, args,
                createWindow: false, warnIfStdErr: false, workingDir: workingDir);

            if (!string.IsNullOrEmpty(stderr) || !File.Exists(pathOutput))
            {
                MessageBox("RunImageConversion failed, stderr = " + stderr);
            }
        }

        public static string RunM4aConversion(string path, string qualitySpec)
        {
            var qualities = new string[] { "16", "24", "96", "128", "144",
                "160", "192", "224", "256", "288", "320", "640", "flac" };
            if (Array.IndexOf(qualities, qualitySpec) == -1)
            {
                throw new CoordinatePicturesException("Unsupported bitrate.");
            }
            else if (!path.EndsWith(".wav", StringComparison.Ordinal))
            {
                throw new CoordinatePicturesException("Unsupported input format.");
            }
            else
            {
                var pathOutput = Path.GetDirectoryName(path) + "\\" +
                    Path.GetFileNameWithoutExtension(path) +
                    (qualitySpec == "flac" ? ".flac" : ".m4a");
                var script = Configs.Current.Get(ConfigKey.FilepathEncodeMusicDropQDirectory) +
                    "\\dropq" + qualitySpec + ".py";
                var args = new string[] { path };
                var stderr = RunPythonScript(script, args, createWindow: false, warnIfStdErr: false);
                if (!File.Exists(pathOutput))
                {
                    MessageBox("RunM4aConversion failed, stderr = " + stderr);
                    return null;
                }
                else
                {
                    return pathOutput;
                }
            }
        }

        public static void JpgStripThumbnails(string path)
        {
            // delete IFD1 tags, removes the Thumbnailimage + all associated tags.
            var exiftool = Configs.Current.Get(ConfigKey.FilepathExifTool);
            var args = new string[] { "-ifd1:all=", "-PreviewImage=", "-overwrite_original", path };
            Run(exiftool, args, hideWindow: true, waitForExit: true, shellExecute: false);
        }

        public static void JpgLosslessOptimize(string path, string pathOut, bool stripAllExif)
        {
            var mozjpeg = Configs.Current.Get(ConfigKey.FilepathMozJpeg);
            var jpegtran = Path.Combine(Path.GetDirectoryName(mozjpeg), "jpegtran.exe");
            var args = new string[] { "-outfile", pathOut, "-optimise",
                "-progressive", "-copy", stripAllExif ? "none" : "all", path };
            Run(jpegtran, args, hideWindow: true, waitForExit: true, shellExecute: false);
        }

        public static void PlayMedia(string path)
        {
            if (path == null)
                path = Path.Combine(Configs.Current.Directory, "silence.flac");

            var player = Configs.Current.Get(ConfigKey.FilepathMediaPlayer);
            if (string.IsNullOrEmpty(player) || !File.Exists(player))
            {
                MessageBox("Media player not found. Go to the main screen " +
                    "and to the option menu and click Options->Set media player location...");
                return;
            }

            var args = player.ToLower().Contains("foobar") ? new string[] { "/playnow", path } :
                new string[] { path };

            Run(player, args, hideWindow: true, waitForExit: false, shellExecute: false);
        }

        public static string GetClipboard()
        {
            try
            {
                return Clipboard.GetText() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static string GetFirstHttpLink(string s)
        {
            foreach (var match in Regex.Matches(s, @"https?://\S+"))
            {
                return ((Match)match).ToString();
            }

            return null;
        }

        // starts website in default browser.
        public static void LaunchUrl(string url)
        {
            string prefix;
            if (url.StartsWith("http://", StringComparison.Ordinal))
            {
                prefix = "http://";
            }
            else if (url.StartsWith("https://", StringComparison.Ordinal))
            {
                prefix = "https://";
            }
            else
            {
                return;
            }

            url = url.Substring(prefix.Length);
            url = url.Replace("%", "%25");
            url = url.Replace("&", "%26");
            url = url.Replace("|", "%7C");
            url = url.Replace("\\", "%5C");
            url = url.Replace("^", "%5E");
            url = url.Replace("\"", "%22");
            url = url.Replace("'", "%27");
            url = url.Replace(">", "%3E");
            url = url.Replace("<", "%3C");
            url = url.Replace(" ", "%20");
            url = prefix + url;
            Process.Start(url);
        }

        // get item from array, clamps index / does not overflow
        public static T ArrayAt<T>(T[] arr, int index)
        {
            if (index < 0)
            {
                return arr[0];
            }
            else if (index >= arr.Length - 1)
            {
                return arr[arr.Length - 1];
            }
            else
            {
                return arr[index];
            }
        }

        public static string GetSha512(string path)
        {
            if (path == null || !File.Exists(path))
            {
                // append the full path: it's not intuitive if all file-not-founds have the same 'hash'.
                return "filenotfound:" + path;
            }

            const int bufSize = 64 * 1024;
            using (SHA512Managed sha512 = new SHA512Managed())
            {
                using (var stream = new BufferedStream(File.OpenRead(path), bufSize))
                {
                    byte[] hash = sha512.ComputeHash(stream);
                    return Convert.ToBase64String(hash);
                }
            }
        }

        public static void RunLongActionInThread(object locking, Control caption,
            Action action, Action actionOnStart = null, Action actionOnComplete = null)
        {
            caption.Text = "Loading...";
            if (actionOnStart != null)
            {
                actionOnStart.Invoke();
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                if (Monitor.TryEnter(locking))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        MessageErr(e.Message);
                    }
                    finally
                    {
                        Monitor.Exit(locking);
                        caption.Invoke((MethodInvoker)(() =>
                        {
                            caption.Text = " ";

                            if (actionOnComplete != null)
                            {
                                actionOnComplete.Invoke();
                            }
                        }));
                    }
                }
                else
                {
                    MessageErr("Please wait for the operation to complete.");
                }
            });
        }

        public static string[] SplitByString(string s, string delim)
        {
            return s.Split(new string[] { delim }, StringSplitOptions.None);
        }

        public static void MessageErr(string msg, bool checkIfSuppressed = false)
        {
            SimpleLog.Current.WriteError(msg);
            MessageBox(msg, checkIfSuppressed);
        }

        public static void MessageBox(string msg, bool checkIfSuppressed = false)
        {
            if (!checkIfSuppressed || !Configs.Current.SuppressDialogs)
            {
                System.Windows.Forms.MessageBox.Show(msg);
            }
        }

        public static bool IsDebug()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    // file list that updates itself when file names are changed.
    public sealed class FileListAutoUpdated : IDisposable
    {
        bool _dirty = true;
        string[] _list = new string[] { };
        FileSystemWatcher _watcher;
        string _baseDir;

        public FileListAutoUpdated(string baseDir, bool recurse)
        {
            Recurse = recurse;
            _baseDir = baseDir;
            _watcher = new FileSystemWatcher(baseDir);
            _watcher.IncludeSubdirectories = recurse;
            _watcher.Created += SetDirty;
            _watcher.Renamed += SetDirty;
            _watcher.Deleted += SetDirty;
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                | NotifyFilters.DirectoryName;
            _watcher.EnableRaisingEvents = true;
        }

        public bool Recurse { get; private set; }

        private void SetDirty(object sender, FileSystemEventArgs e)
        {
            _dirty = true;
        }

        public void Dirty()
        {
            _dirty = true;
        }

        public string[] GetList(bool forceRefresh = false)
        {
            if (_dirty || forceRefresh)
            {
                // DirectoryInfo takes about 13ms, for a 900 file directory
                // Directory.EnumerateFiles takes about 12ms
                var enumerator = Directory.EnumerateFiles(_baseDir, "*",
                    Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                _list = enumerator.ToArray();
                Array.Sort(_list, StringComparer.OrdinalIgnoreCase);
                _dirty = false;
            }

            return _list;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_watcher != null)
                {
                    _watcher.Dispose();
                }
            }
        }
    }

    // navigates a FileListAutoUpdated in alphabetical order.
    // gracefully handles the case when navigating to a file that was just deleted, and
    // FileListAutoUpdated has not yet received the notification event.
    public sealed class FileListNavigation : IDisposable
    {
        string[] _extensionsAllowed;
        bool _excludeMarked;
        FileListAutoUpdated _list;
        public FileListNavigation(string baseDir, string[] extensionsAllowed,
             bool recurse, bool excludeMarked = true, string sCurrent = "")
        {
            BaseDirectory = baseDir;
            _extensionsAllowed = extensionsAllowed;
            _list = new FileListAutoUpdated(baseDir, recurse);
            _excludeMarked = excludeMarked;
            TrySetPath(sCurrent);
        }

        public string Current { get; private set; }
        public string BaseDirectory { get; private set; }

        public void Refresh()
        {
            _list = new FileListAutoUpdated(BaseDirectory, _list.Recurse);
            TrySetPath("");
        }

        public void NotifyFileChanges()
        {
            _list.Dirty();
        }

        // try an action twice if necessary.
        // if the filepath we are given no longer exists,
        // FileListAutoUpdated might have not received the notification event yet,
        // so tell it to refresh and retry once more.
        void TryAgainIfFileIsMissing(Func<string[], string> fn)
        {
            var list = GetList();
            if (list.Length == 0)
            {
                Current = null;
                return;
            }

            string firstTry = fn(list);
            if (firstTry != null && !File.Exists(firstTry))
            {
                // refresh the list and try again
                list = GetList(true);
                if (list.Length == 0)
                {
                    Current = null;
                    return;
                }

                Current = fn(list);
            }
            else
            {
                Current = firstTry;
            }
        }

        static int GetLessThanOrEqual(string[] list, string search)
        {
            var index = Array.BinarySearch(list, search, StringComparer.OrdinalIgnoreCase);
            if (index < 0)
            {
                index = ~index - 1;
            }

            return index;
        }

        public void GoNextOrPrev(bool isNext, List<string> neighbors = null,
            int retrieveNeighbors = 0)
        {
            TryAgainIfFileIsMissing((list) =>
            {
                var index = GetLessThanOrEqual(list, Current ?? "");
                if (isNext)
                {
                    // caller has asked us to return adjacent items
                    for (int i = 0; i < retrieveNeighbors; i++)
                    {
                        neighbors[i] = Utils.ArrayAt(list, index + i + 2);
                    }

                    return Utils.ArrayAt(list, index + 1);
                }
                else
                {
                    // index is LessThanOrEqual, but we want strictly LessThan
                    // so move prev if equal.
                    if (index > 0 && Current == list[index])
                    {
                        index--;
                    }

                    // caller has asked us to return adjacent items
                    for (int i = 0; i < retrieveNeighbors; i++)
                    {
                        neighbors[i] = Utils.ArrayAt(list, index - i - 1);
                    }

                    return Utils.ArrayAt(list, index);
                }
            });
        }

        public void GoFirst()
        {
            TryAgainIfFileIsMissing((list) =>
            {
                return list[0];
            });
        }

        public void GoLast()
        {
            TryAgainIfFileIsMissing((list) =>
            {
                return list[list.Length - 1];
            });
        }

        public void TrySetPath(string current, bool verify = true)
        {
            Current = current;
            if (verify)
            {
                TryAgainIfFileIsMissing((list) =>
                {
                    var index = GetLessThanOrEqual(list, Current ?? "");
                    return Utils.ArrayAt(list, index);
                });
            }
        }

        public string[] GetList(bool forceRefresh = false, bool includeMarked = false)
        {
            Func<string, bool> includeFile = (path) =>
            {
                if (!includeMarked && _excludeMarked && path.Contains(FilenameUtils.MarkerString))
                    return false;
                else if (!FilenameUtils.IsExtensionInList(path, _extensionsAllowed))
                    return false;
                else
                    return true;
            };

            return _list.GetList(forceRefresh).Where(includeFile).ToArray();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_list != null)
                {
                    _list.Dispose();
                }
            }
        }
    }

    public static class FilenameUtils
    {
        public static readonly string MarkerString = "__MARKAS__";

        public static bool LooksLikeImage(string filepath)
        {
            return IsExtensionInList(filepath, new string[] { ".jpg", ".png",
                ".gif", ".bmp", ".webp", ".emf", ".wmf", ".jpeg" });
        }

        public static bool LooksLikeAudio(string filepath)
        {
            return IsExtensionInList(filepath, new string[] { ".wav", ".flac",
                ".mp3", ".m4a", ".mp4" });
        }

        public static bool IsExtensionInList(string filepath, string[] extensions)
        {
            var filepathLower = filepath.ToLowerInvariant();
            foreach (var item in extensions)
            {
                if (filepathLower.EndsWith(item, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsExt(string filepath, string extension)
        {
            return filepath.ToLowerInvariant().EndsWith(extension, StringComparison.Ordinal);
        }

        public static int NumberedPrefixLength()
        {
            return "([0000])".Length;
        }

        public static string AddNumberedPrefix(string filepath, int number)
        {
            var nameOnly = Path.GetFileName(filepath);
            if (nameOnly != GetFileNameWithoutNumberedPrefix(filepath))
            {
                // already has one
                return filepath;
            }
            else
            {
                // add a trailing zero, just lets the user change the order more easily.
                return Path.GetDirectoryName(filepath) +
                    "\\([" + number.ToString("D3") + "0])" + nameOnly;
            }
        }

        public static string GetFileNameWithoutNumberedPrefix(string filepath)
        {
            var nameOnly = Path.GetFileName(filepath);
            if (nameOnly.Length > NumberedPrefixLength() &&
                nameOnly.StartsWith("([", StringComparison.Ordinal) &&
                nameOnly.Substring(6, 2) == "])")
            {
                return nameOnly.Substring(NumberedPrefixLength());
            }
            else
            {
                return nameOnly;
            }
        }

        public static string AddCategoryToFilename(string path, string category)
        {
            if (path.Contains(MarkerString))
            {
                Utils.MessageErr("Path " + path + " already contains marker.");
                return path;
            }

            var ext = Path.GetExtension(path);
            var before = Path.GetFileNameWithoutExtension(path);
            return Path.Combine(Path.GetDirectoryName(path), before) +
                MarkerString + category + ext;
        }

        public static void GetCategoryFromFilename(string pathAndCategory,
            out string pathWithoutCategory, out string category)
        {
            // check nothing in path has mark
            if (Path.GetDirectoryName(pathAndCategory).Contains(MarkerString))
            {
                throw new CoordinatePicturesException("Directories should not have marker");
            }

            var parts = Utils.SplitByString(pathAndCategory, MarkerString);
            if (parts.Length != 2)
            {
                Utils.MessageErr("Path " + pathAndCategory +
                    " should contain exactly 1 marker.", true);

                throw new CoordinatePicturesException("Path " + pathAndCategory +
                    " should contain exactly 1 marker.");
            }

            var partsAfterMarker = parts[1].Split(new char[] { '.' });
            if (partsAfterMarker.Length != 2)
            {
                throw new CoordinatePicturesException(
                    "Parts after the marker shouldn't have another .");
            }

            category = partsAfterMarker[0];
            pathWithoutCategory = parts[0] + "." + partsAfterMarker[1];
        }

        public static bool SameExceptExtension(string filepath1, string filepath2)
        {
            var rootNoExtension1 = Path.Combine(
                Path.GetDirectoryName(filepath1), Path.GetFileNameWithoutExtension(filepath1));
            var rootNoExtension2 = Path.Combine(
                Path.GetDirectoryName(filepath2), Path.GetFileNameWithoutExtension(filepath2));

            return rootNoExtension1.ToUpperInvariant() == rootNoExtension2.ToUpperInvariant();
        }

        public static bool IsPathRooted(string filepath)
        {
            try
            {
                return Path.IsPathRooted(filepath);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    // Simple logging class, writes synchronously to a text file.
    public sealed class SimpleLog
    {
        private const int CheckFileSizePeriod = 32;
        private static SimpleLog _instance;
        string _path;
        int _maxFileSize;
        int _counter;
        public SimpleLog(string path, int maxFileSize = 4 * 1024 * 1024)
        {
            _path = path;
            _maxFileSize = maxFileSize;
        }

        public static SimpleLog Current
        {
            get
            {
                return _instance;
            }
        }

        public static void Init(string path)
        {
            _instance = new SimpleLog(path);
        }

        public void WriteLog(string s)
        {
            // rather than cycling logging, delete previous logs for simplicity.
            _counter++;
            if (_counter > CheckFileSizePeriod)
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > _maxFileSize)
                {
                    File.Delete(_path);
                }

                _counter = 0;
            }

            try
            {
                File.AppendAllText(_path, Environment.NewLine + s);
            }
            catch (Exception)
            {
                if (!Utils.AskToConfirm("Could not write to " + _path +
                    "; labs_coordinate_pictures.exe currently needs to be " +
                    "in writable directory. Continue?"))
                {
                    Environment.Exit(1);
                }
            }
        }

        public void WriteWarning(string s)
        {
            WriteLog("[warning] " + s);
        }

        public void WriteError(string s)
        {
            WriteLog("[error] " + s);
        }

        public void WriteVerbose(string s)
        {
            if (Configs.Current.GetBool(ConfigKey.EnableVerboseLogging))
            {
                WriteLog("[vb] " + s);
            }
        }
    }

    // finds similar filenames, especially those created by FormGallery::convertToSeveralJpgs.
    // e.g., given example.png90.jpg, will see that
    // example.png, example_out.png and example.png60.jpg are related files.
    public static class FindSimilarFilenames
    {
        public static bool FindPathWithSuffixRemoved(string path, string[] extensions,
            out string pathWithSuffixRemoved)
        {
            pathWithSuffixRemoved = null;
            var filenameParts = Path.GetFileName(path).Split(new char[] { '.' });
            if (filenameParts.Length > 2)
            {
                var middle = filenameParts[filenameParts.Length - 2].ToLowerInvariant();
                bool found = false;
                foreach (var fileExt in extensions)
                {
                    var type = fileExt.Replace(".", "");
                    if (middle.StartsWith(type, StringComparison.Ordinal) &&
                        Utils.IsDigits(middle.Replace(type, "")))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    var list = new List<string>(filenameParts);
                    list.RemoveAt(list.Count - 2);
                    pathWithSuffixRemoved = Path.GetDirectoryName(path) +
                        "\\" + string.Join(".", list);

                    return true;
                }
            }

            return false;
        }

        public static List<string> FindSimilarNames(string path, string[] types,
            string[] otherFiles, out bool nameHasSuffix, out string pathWithoutSuffix)
        {
            // parse the file
            pathWithoutSuffix = null;
            nameHasSuffix = FindPathWithSuffixRemoved(path, types, out pathWithoutSuffix);

            // delete all the rest in group
            var filenameWithoutSuffix = nameHasSuffix ? pathWithoutSuffix : path;
            List<string> results = new List<string>();
            foreach (var otherFile in otherFiles)
            {
                if (otherFile.ToUpperInvariant() != path.ToUpperInvariant())
                {
                    string nameMiddleRemoved;
                    if (FilenameUtils.SameExceptExtension(filenameWithoutSuffix, otherFile) ||
                        (FindPathWithSuffixRemoved(otherFile, types, out nameMiddleRemoved) &&
                        FilenameUtils.SameExceptExtension(filenameWithoutSuffix, nameMiddleRemoved)))
                    {
                        results.Add(otherFile);
                    }
                }
            }

            return results;
        }
    }

    public class UndoStack<T>
    {
        List<T> _list = new List<T>();
        int _position = -1;

        public void Add(T current)
        {
            // if we are here after having called undo,
            // invalidate items higher on the stack
            _list.RemoveRange(_position + 1, (_list.Count - _position) - 1);

            // add to stack
            _list.Add(current);
            _position = _list.Count - 1;
        }

        public T PeekUndo()
        {
            if (_position >= 0)
                return _list[_position];
            else
                return default(T);
        }

        public void Undo()
        {
            if (_position >= 0)
                --_position;
        }

        public T PeekRedo()
        {
            if (_position + 1 <= _list.Count - 1)
                return _list[_position + 1];
            else
                return default(T);
        }

        public void Redo()
        {
            if (_position + 1 <= _list.Count - 1)
                ++_position;
        }
    }

    [Serializable]
    public class CoordinatePicturesException : Exception
    {
        public CoordinatePicturesException(string message, Exception e)
            : base("CoordinatePicturesException " + message, e)
        {
        }

        public CoordinatePicturesException(string message)
            : this(message, null)
        {
        }

        public CoordinatePicturesException()
            : this("", null)
        {
        }

        protected CoordinatePicturesException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
    }
}
