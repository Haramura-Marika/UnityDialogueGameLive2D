using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AI.Chat
{
    public static class FileDialogHelper
    {
        private static bool isDialogOpen;

        public static string ShowOpenFileDialog(string title, string extension = "json", string initialDirectory = null)
        {
            if (isDialogOpen)
            {
                Debug.LogWarning("[FileDialogHelper] 文件对话框已在打开中，忽略重复请求");
                return null;
            }

            isDialogOpen = true;
            try
            {
#if UNITY_EDITOR
                string dir = ResolveInitialDirectory(initialDirectory);
                string ext = string.IsNullOrEmpty(extension) ? string.Empty : extension.TrimStart('.');
                return EditorUtility.OpenFilePanel(string.IsNullOrEmpty(title) ? "\u6253\u5f00\u6587\u4ef6" : title, dir, ext);
#elif UNITY_STANDALONE_WIN
                return ShowWindowsOpenFileDialog(title, extension, initialDirectory);
#else
                Debug.LogWarning("[FileDialogHelper] \u5f53\u524d\u5e73\u53f0\u672a\u63d0\u4f9b\u6587\u4ef6\u9009\u62e9\u5bf9\u8bdd\u6846\uff0c\u8fd4\u56de null\u3002");
                return null;
#endif
            }
            finally
            {
                isDialogOpen = false;
            }
        }

        public static string ShowSaveFileDialog(string title, string extension = "json", string initialDirectory = null, string defaultFileName = "save")
        {
            if (isDialogOpen)
            {
                Debug.LogWarning("[FileDialogHelper] 文件对话框已在打开中，忽略重复请求");
                return null;
            }

            isDialogOpen = true;
            try
            {
#if UNITY_EDITOR
                string dir = ResolveInitialDirectory(initialDirectory);
                string ext = string.IsNullOrEmpty(extension) ? string.Empty : extension.TrimStart('.');
                string file = string.IsNullOrEmpty(defaultFileName) ? "save" : defaultFileName;
                return EditorUtility.SaveFilePanel(string.IsNullOrEmpty(title) ? "\u4fdd\u5b58\u6587\u4ef6" : title, dir, file, ext);
#elif UNITY_STANDALONE_WIN
                return ShowWindowsSaveFileDialog(title, extension, initialDirectory, defaultFileName);
#else
                Debug.LogWarning("[FileDialogHelper] \u5f53\u524d\u5e73\u53f0\u672a\u63d0\u4f9b\u6587\u4ef6\u4fdd\u5b58\u5bf9\u8bdd\u6846\uff0c\u8fd4\u56de null\u3002");
                return null;
#endif
            }
            finally
            {
                isDialogOpen = false;
            }
        }

        public static bool ShowConfirmationDialog(string title, string message)
        {
#if UNITY_EDITOR
            return EditorUtility.DisplayDialog(
                string.IsNullOrEmpty(title) ? "\u786e\u8ba4" : title,
                string.IsNullOrEmpty(message) ? "\u662f\u5426\u7ee7\u7eed\uff1f" : message,
                "\u7ee7\u7eed",
                "\u53d6\u6d88");
#elif UNITY_STANDALONE_WIN
            int result = MessageBox(
                GetDialogOwner(),
                string.IsNullOrEmpty(message) ? "\u662f\u5426\u7ee7\u7eed\uff1f" : message,
                string.IsNullOrEmpty(title) ? "\u786e\u8ba4" : title,
                MessageBoxOkCancel | MessageBoxIconWarning);
            return result == MessageBoxOk;
#else
            Debug.LogWarning("[FileDialogHelper] \u5f53\u524d\u5e73\u53f0\u672a\u63d0\u4f9b\u786e\u8ba4\u5bf9\u8bdd\u6846\uff0c\u9ed8\u8ba4\u8fd4\u56de false\u3002");
            return false;
#endif
        }

        public static void ShowMessageDialog(string title, string message)
        {
#if UNITY_EDITOR
            EditorUtility.DisplayDialog(
                string.IsNullOrEmpty(title) ? "\u63d0\u793a" : title,
                string.IsNullOrEmpty(message) ? "\u64cd\u4f5c\u5b8c\u6210\u3002" : message,
                "\u786e\u5b9a");
#elif UNITY_STANDALONE_WIN
            MessageBox(
                GetDialogOwner(),
                string.IsNullOrEmpty(message) ? "\u64cd\u4f5c\u5b8c\u6210\u3002" : message,
                string.IsNullOrEmpty(title) ? "\u63d0\u793a" : title,
                MessageBoxOkOnly | MessageBoxIconInformation);
#else
            Debug.LogWarning($"[FileDialogHelper] {title}: {message}");
#endif
        }

        private static string ResolveInitialDirectory(string initialDirectory)
        {
            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                return initialDirectory;
            }

            if (Directory.Exists(Application.dataPath))
            {
                return Application.dataPath;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static string BuildFileFilter(string extension)
        {
            string ext = string.IsNullOrEmpty(extension) ? string.Empty : extension.Trim().TrimStart('.');
            if (string.IsNullOrEmpty(ext))
            {
                return "All Files (*.*)|*.*";
            }

            return $"{ext.ToUpperInvariant()} Files (*.{ext})|*.{ext}|All Files (*.*)|*.*";
        }

#if UNITY_STANDALONE_WIN
        private const int OpenFileNamePathMustExist = 0x00000800;
        private const int OpenFileNameFileMustExist = 0x00001000;
        private const int OpenFileNameNoChangeDir = 0x00000008;
        private const int OpenFileNameOverwritePrompt = 0x00000002;
        private const int MessageBoxOkOnly = 0x00000000;
        private const int MessageBoxOkCancel = 0x00000001;
        private const int MessageBoxIconWarning = 0x00000030;
        private const int MessageBoxIconInformation = 0x00000040;
        private const int MessageBoxOk = 1;
        private const int MaxPathLength = 2048;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public StringBuilder lpstrFile;
            public int nMaxFile;
            public StringBuilder lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName([In, Out] ref OpenFileName openFileName);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileName([In, Out] ref OpenFileName openFileName);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static string ShowWindowsOpenFileDialog(string title, string extension, string initialDirectory)
        {
            return ShowPowerShellFileDialog(
                isSaveDialog: false,
                title: title,
                extension: extension,
                initialDirectory: initialDirectory,
                defaultFileName: null);
        }

        private static string ShowWindowsSaveFileDialog(string title, string extension, string initialDirectory, string defaultFileName)
        {
            return ShowPowerShellFileDialog(
                isSaveDialog: true,
                title: title,
                extension: extension,
                initialDirectory: initialDirectory,
                defaultFileName: defaultFileName);
        }

        private static string ShowPowerShellFileDialog(bool isSaveDialog, string title, string extension, string initialDirectory, string defaultFileName)
        {
            string outputPath = Path.Combine(Path.GetTempPath(), $"live2d_dialog_{Guid.NewGuid():N}.txt");

            try
            {
                string dialogType = isSaveDialog ? "SaveFileDialog" : "OpenFileDialog";
                string escapedTitle = EscapePowerShellSingleQuotedString(string.IsNullOrEmpty(title) ? "选择文件" : title);
                string escapedDirectory = EscapePowerShellSingleQuotedString(ResolveInitialDirectory(initialDirectory));
                string escapedFilter = EscapePowerShellSingleQuotedString(BuildFileFilter(extension));
                string escapedOutputPath = EscapePowerShellSingleQuotedString(outputPath);
                string escapedDefaultFileName = EscapePowerShellSingleQuotedString(defaultFileName ?? string.Empty);

                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine("$ErrorActionPreference = 'Stop'");
                scriptBuilder.AppendLine("Add-Type -AssemblyName System.Windows.Forms");
                scriptBuilder.AppendLine("$dialog = New-Object System.Windows.Forms." + dialogType);
                scriptBuilder.AppendLine("$dialog.Title = '" + escapedTitle + "'");
                scriptBuilder.AppendLine("$dialog.InitialDirectory = '" + escapedDirectory + "'");
                scriptBuilder.AppendLine("$dialog.Filter = '" + escapedFilter + "'");
                scriptBuilder.AppendLine("$dialog.RestoreDirectory = $true");

                if (isSaveDialog)
                {
                    scriptBuilder.AppendLine("$dialog.OverwritePrompt = $true");
                    if (!string.IsNullOrEmpty(defaultFileName))
                    {
                        scriptBuilder.AppendLine("$dialog.FileName = '" + escapedDefaultFileName + "'");
                    }
                }
                else
                {
                    scriptBuilder.AppendLine("$dialog.CheckFileExists = $true");
                }

                scriptBuilder.AppendLine("if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {");
                scriptBuilder.AppendLine("    Set-Content -LiteralPath '" + escapedOutputPath + "' -Value $dialog.FileName -Encoding UTF8");
                scriptBuilder.AppendLine("}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = GetPowerShellExecutablePath(),
                    Arguments = "-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand " + EncodePowerShellCommand(scriptBuilder.ToString()),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Debug.LogError("[FileDialogHelper] Failed to start PowerShell for file dialog.");
                        return null;
                    }

                    string errorOutput = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Debug.LogError($"[FileDialogHelper] PowerShell file dialog failed with exit code {process.ExitCode}: {errorOutput}");
                        return null;
                    }
                }

                if (!File.Exists(outputPath))
                {
                    return null;
                }

                string selectedPath = File.ReadAllText(outputPath, Encoding.UTF8).Trim();
                return string.IsNullOrEmpty(selectedPath) ? null : selectedPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileDialogHelper] PowerShell file dialog failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        private static string GetPowerShellExecutablePath()
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string candidate = Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(candidate) ? candidate : "powershell.exe";
        }

        private static string EncodePowerShellCommand(string command)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(command);
            return Convert.ToBase64String(bytes);
        }

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static OpenFileName CreateWindowsDialog(string title, string extension, string initialDirectory, string defaultFileName, int flags)
        {
            string ext = string.IsNullOrEmpty(extension) ? string.Empty : extension.Trim().TrimStart('.');
            string fullDefaultName = string.IsNullOrEmpty(defaultFileName) || string.IsNullOrEmpty(ext)
                ? (defaultFileName ?? string.Empty)
                : (defaultFileName.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase) ? defaultFileName : $"{defaultFileName}.{ext}");

            var fileBuffer = new StringBuilder(MaxPathLength);
            if (!string.IsNullOrEmpty(fullDefaultName))
            {
                fileBuffer.Append(fullDefaultName);
            }

            return new OpenFileName
            {
                lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                hwndOwner = GetDialogOwner(),
                lpstrFilter = BuildWindowsFilter(extension),
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxPathLength,
                lpstrFileTitle = new StringBuilder(256),
                nMaxFileTitle = 256,
                lpstrInitialDir = ResolveInitialDirectory(initialDirectory),
                lpstrTitle = string.IsNullOrEmpty(title) ? "\u9009\u62e9\u6587\u4ef6" : title,
                Flags = flags,
                lpstrDefExt = ext
            };
        }

        private static string BuildWindowsFilter(string extension)
        {
            string ext = string.IsNullOrEmpty(extension) ? string.Empty : extension.Trim().TrimStart('.');
            if (string.IsNullOrEmpty(ext))
            {
                return "All Files (*.*)\0*.*\0\0";
            }

            string upperExt = ext.ToUpperInvariant();
            return $"{upperExt} Files (*.{ext})\0*.{ext}\0All Files (*.*)\0*.*\0\0";
        }

        private static IntPtr GetDialogOwner()
        {
            IntPtr activeWindow = GetActiveWindow();
            return activeWindow != IntPtr.Zero ? activeWindow : GetForegroundWindow();
        }

        private static string TryShowWinFormsOpenFileDialog(string title, string extension, string initialDirectory)
        {
            return TryShowWinFormsFileDialog(
                "System.Windows.Forms.OpenFileDialog",
                title,
                extension,
                initialDirectory,
                null,
                "CheckFileExists");
        }

        private static string TryShowWinFormsSaveFileDialog(string title, string extension, string initialDirectory, string defaultFileName)
        {
            return TryShowWinFormsFileDialog(
                "System.Windows.Forms.SaveFileDialog",
                title,
                extension,
                initialDirectory,
                defaultFileName,
                null);
        }

        private static string TryShowWinFormsFileDialog(string dialogTypeName, string title, string extension, string initialDirectory, string defaultFileName, string boolPropertyName)
        {
            try
            {
                Type dialogType = Type.GetType(dialogTypeName + ", System.Windows.Forms");
                if (dialogType == null)
                {
                    return null;
                }

                object dialog = Activator.CreateInstance(dialogType);
                if (dialog == null)
                {
                    return null;
                }

                try
                {
                    SetProperty(dialogType, dialog, "Title", string.IsNullOrEmpty(title) ? "\u9009\u62e9\u6587\u4ef6" : title);
                    SetProperty(dialogType, dialog, "InitialDirectory", ResolveInitialDirectory(initialDirectory));
                    SetProperty(dialogType, dialog, "Filter", BuildFileFilter(extension));
                    SetProperty(dialogType, dialog, "RestoreDirectory", true);

                    if (!string.IsNullOrEmpty(boolPropertyName))
                    {
                        SetProperty(dialogType, dialog, boolPropertyName, true);
                    }

                    if (!string.IsNullOrEmpty(defaultFileName))
                    {
                        SetProperty(dialogType, dialog, "FileName", defaultFileName);
                    }

                    object result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
                    if (result == null || !string.Equals(result.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    return dialogType.GetProperty("FileName")?.GetValue(dialog)?.ToString();
                }
                finally
                {
                    if (dialog is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileDialogHelper] WinForms dialog fallback failed: {ex.Message}");
                return null;
            }
        }

        private static void SetProperty(Type dialogType, object dialog, string propertyName, object value)
        {
            var property = dialogType.GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(dialog, value);
            }
        }
#endif
    }
}
