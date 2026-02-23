using System;
using System.Linq;
using System.Reflection;

namespace AI.Providers
{
    /// <summary>
    /// 通过反射读取 AI.Config.AIAPISettings 中的各 Provider 配置
    /// </summary>
    internal static class ChatConfig
    {
        private static Type GetSettingsType()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var a in asm)
                {
                    var t = a.GetType("AI.Config.AIAPISettings");
                    if (t != null) return t;
                }
            }
            catch { }
            return null;
        }

        private static object GetSettingsInstance()
        {
            var t = GetSettingsType();
            if (t == null) return null;
            var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            return prop?.GetValue(null, null);
        }

        private static object GetServiceConfig(string propName)
        {
            var inst = GetSettingsInstance();
            if (inst == null) return null;
            var p = inst.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(inst, null);
        }

        public static string GetApiKey(string serviceProp)
        {
            var cfg = GetServiceConfig(serviceProp);
            if (cfg == null) return null;
            var apiKeyProp = cfg.GetType().GetProperty("ApiKey", BindingFlags.Public | BindingFlags.Instance);
            return apiKeyProp?.GetValue(cfg, null) as string;
        }

        public static string GetModel(string serviceProp, string @default)
        {
            var cfg = GetServiceConfig(serviceProp);
            if (cfg == null) return @default;
            var modelProp = cfg.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.Instance);
            var v = modelProp?.GetValue(cfg, null) as string;
            return string.IsNullOrEmpty(v) ? @default : v;
        }

        public static float GetTemperature(string serviceProp, float @default)
        {
            var cfg = GetServiceConfig(serviceProp);
            if (cfg == null) return @default;
            var prop = cfg.GetType().GetProperty("Temperature", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return @default;
            try
            {
                var v = prop.GetValue(cfg, null);
                return v is float f ? f : @default;
            }
            catch { return @default; }
        }

        public static int GetMaxTokens(string serviceProp, int @default)
        {
            var cfg = GetServiceConfig(serviceProp);
            if (cfg == null) return @default;
            var prop = cfg.GetType().GetProperty("MaxTokens", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return @default;
            try
            {
                var v = prop.GetValue(cfg, null);
                return v is int i ? i : @default;
            }
            catch { return @default; }
        }

        public static bool GetStreamingEnabled(string serviceProp, bool @default)
        {
            var cfg = GetServiceConfig(serviceProp);
            if (cfg == null) return @default;
            var prop = cfg.GetType().GetProperty("StreamingEnabled", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return @default;
            try
            {
                var v = prop.GetValue(cfg, null);
                return v is bool b ? b : @default;
            }
            catch { return @default; }
        }
    }
}
