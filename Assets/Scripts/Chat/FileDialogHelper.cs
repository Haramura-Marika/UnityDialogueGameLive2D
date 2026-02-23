using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AI.Chat
{
    public static class FileDialogHelper
    {
        public static string ShowOpenFileDialog(string title, string extension = "json", string initialDirectory = null)
        {
#if UNITY_EDITOR
            string dir = string.IsNullOrEmpty(initialDirectory) ? Application.persistentDataPath : initialDirectory;
            string ext = string.IsNullOrEmpty(extension) ? "" : extension.TrimStart('.');
            return EditorUtility.OpenFilePanel(string.IsNullOrEmpty(title) ? "打开文件" : title, dir, ext);
#else
            Debug.LogWarning("[FileDialogHelper] 当前平台未提供文件选择对话框，返回 null。");
            return null;
#endif
        }

        public static string ShowSaveFileDialog(string title, string extension = "json", string initialDirectory = null, string defaultFileName = "save")
        {
#if UNITY_EDITOR
            string dir = string.IsNullOrEmpty(initialDirectory) ? Application.persistentDataPath : initialDirectory;
            string ext = string.IsNullOrEmpty(extension) ? "" : extension.TrimStart('.');
            string file = string.IsNullOrEmpty(defaultFileName) ? "save" : defaultFileName;
            string path = EditorUtility.SaveFilePanel(string.IsNullOrEmpty(title) ? "保存文件" : title, dir, file, ext);
            return path;
#else
            Debug.LogWarning("[FileDialogHelper] 当前平台未提供文件保存对话框，返回 null。");
            return null;
#endif
        }
    }
}
