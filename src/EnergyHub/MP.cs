using System;
using System.IO;
using System.Reflection;

namespace EnergyHub
{
    /// <summary>
    /// 联机（NebulaMultiplayerMod）软兼容层。
    ///
    /// 设计约束：主程序集**不能**引用 NebulaAPI / NebulaWorld。
    /// 否则没装联机框架的玩家，BepInEx 在扫描插件类型时会因为基类解析失败
    /// （ReflectionTypeLoadException）而把整个 DSPEnergyHub.dll 跳过，MOD 直接不加载。
    ///
    /// 所以所有 Nebula 相关代码都放在可选的子模块 DSPEnergyHub.Nebula.bin 里，
    /// 由本类在运行时按需 Assembly.Load(byte[]) 载入，再用反射调用。
    /// 没装 Nebula 时：子模块根本不加载，所有成员返回单机语义，零开销、零副作用。
    /// </summary>
    public static class MP
    {
        /// <summary>系统里是否存在 NebulaAPI（装过 Nebula 就会是 true，但不代表正在联机）。</summary>
        public static bool HasNebula;

        /// <summary>当前是否处于多人会话中。</summary>
        public static bool Active;

        /// <summary>本端是否为客户端（非主机）。</summary>
        public static bool IsClient;

        /// <summary>
        /// 本端是否负责结算。单机恒为 true；联机时只有主机为 true。
        /// HubManager 的结算逻辑（充放电 / 扣料 / 升级 / 账本）全靠它 gate。
        /// </summary>
        public static bool IsHost { get { return !Active || !IsClient; } }

        private static MethodInfo _mActive;
        private static MethodInfo _mIsClient;
        private static MethodInfo _mBroadcast;
        private static bool _loaded;
        private static bool _broken;          // 反射链路出过一次错就彻底停用，避免每帧刷日志
        private static float _timer;
        private static float _retry;

        private const float PollSec = 0.5f;

        // ---------------- 初始化 / 轮询 ----------------

        public static void Init()
        {
            TryLoad();
            if (_loaded)
                Plugin.Log.LogInfo("联机兼容层已就绪（Nebula 已安装，结算将以主机为准）");
            else if (HasNebula)
                Plugin.Log.LogWarning("检测到 Nebula，但联机子模块加载失败（DSPEnergyHub.Nebula.bin 缺失？）——按单机运行");
        }

        /// <summary>每帧调用（内部 0.5 秒采样一次，反射开销可忽略）。</summary>
        public static void Poll(float dt)
        {
            if (_broken)
                return;

            _timer += dt;
            if (_timer < PollSec)
                return;
            _timer = 0f;

            if (!_loaded)
            {
                _retry -= PollSec;
                if (_retry <= 0f)
                {
                    _retry = 3f;
                    TryLoad();
                }
                return;
            }

            try
            {
                Active = (bool)_mActive.Invoke(null, null);
                IsClient = Active && (bool)_mIsClient.Invoke(null, null);
            }
            catch (Exception e)
            {
                _broken = true;
                Active = false;
                IsClient = false;
                Plugin.Log.LogWarning("联机状态探测失败，回退单机模式: " + e.Message);
            }
        }

        /// <summary>主机调用：把本机所有枢纽状态广播给客户端（内部自带节流）。</summary>
        public static void Broadcast(float dt)
        {
            if (_broken || !_loaded || !Active || IsClient || _mBroadcast == null)
                return;
            try
            {
                _mBroadcast.Invoke(null, new object[] { dt });
            }
            catch (Exception e)
            {
                _broken = true;
                Plugin.Log.LogWarning("联机状态广播失败，本局不再同步: " + e.Message);
            }
        }

        // ---------------- 载入可选子模块 ----------------

        private static void TryLoad()
        {
            if (_loaded || _broken)
                return;

            try
            {
                if (FindNebulaApi() == null)
                    return;     // 没装 Nebula：正常单机，不加载子模块

                HasNebula = true;

                string dir = Path.GetDirectoryName(typeof(MP).Assembly.Location);
                if (string.IsNullOrEmpty(dir))
                    return;

                // 用 .bin 后缀：BepInEx 不会把子模块当成插件去扫描，
                // 没装 Nebula 时它连碰都不会被碰到。
                string path = Path.Combine(dir, "DSPEnergyHub.Nebula.bin");
                if (!File.Exists(path))
                    return;

                Assembly asm = Assembly.Load(File.ReadAllBytes(path));
                Type bridge = asm.GetType("EnergyHub.Nebula.NebulaBridge", false);
                if (bridge == null)
                    return;

                MethodInfo init = bridge.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                _mActive = bridge.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Static);
                _mIsClient = bridge.GetMethod("IsClient", BindingFlags.Public | BindingFlags.Static);
                _mBroadcast = bridge.GetMethod("Broadcast", BindingFlags.Public | BindingFlags.Static);

                if (init == null || _mActive == null || _mIsClient == null || _mBroadcast == null)
                {
                    Plugin.Log.LogWarning("联机子模块入口不完整，按单机运行");
                    return;
                }

                init.Invoke(null, null);
                _loaded = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("加载联机子模块失败（按单机运行）: " + e.Message);
            }
        }

        /// <summary>
        /// 查找 NebulaAPI 程序集。BepInEx 会预加载 plugins 目录下的所有 dll，
        /// 所以正常情况下它已经在 AppDomain 里；找不到就说明玩家没装 Nebula。
        /// </summary>
        private static Assembly FindNebulaApi()
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Assembly a = asms[i];
                if (a == null)
                    continue;
                string n = a.GetName().Name;
                if (n == "NebulaAPI")
                    return a;
            }
            return null;
        }
    }
}
