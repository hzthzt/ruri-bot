using System;
using System.Threading.Tasks;
using RuriBot.Core;
using RuriBot.Library.Log;
using NapCatSharpLib.Message;

namespace RuriBot.Runner
{
    class Program
    {
        /// <summary>
        /// ruri-bot 启动入口
        /// 用法: RuriBot.Runner.exe [ip] [port]
        /// 默认连接 127.0.0.1:3001（NapCatQQ 默认正向 WebSocket 端口）
        /// </summary>
        static async Task Main(string[] args)
        {
            string ip = args.Length >= 1 ? args[0] : "127.0.0.1";
            int port = args.Length >= 2 ? int.Parse(args[1]) : 3001;

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 处理中文系统上 .NET 6 资源程序集缺失的问题
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                if (args.Name.Contains(".resources,"))
                    return null; // 回退到英文资源
                return null;
            };

            Console.WriteLine("========================================");
            Console.WriteLine("  ruri-bot QQ 机器人框架");
            Console.WriteLine("========================================");
            Console.WriteLine($"  连接目标: ws://{ip}:{port}");
            Console.WriteLine("========================================");

            var logger = new ConsoleLogger();
            RRBotCore core = null;

            try
            {
                // 创建核心实例（构造函数内自动初始化全部子系统）
                core = new RRBotCore(ip, port, logger);

                // 启动 WebSocket 连接
                core.Start();
                Console.WriteLine("[信息] 正在连接 NapCatQQ...");

                // 等待 WebSocket 连接建立（最多等 10 秒）
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (core.IsConnected)
                    {
                        Console.WriteLine("[信息] WebSocket 连接成功！");
                        break;
                    }
                    if (i % 4 == 3)
                        Console.WriteLine("[信息] 等待连接中...");
                }

                if (core.IsConnected)
                {
                    // 连接成功后获取登录信息
                    var loginInfo = await core.GetLoginInfoAsync();
                    if (loginInfo != null)
                    {
                        Console.WriteLine("========================================");
                        Console.WriteLine($"  登录账号: {loginInfo.nickname}");
                        Console.WriteLine($"  QQ 号码: {loginInfo.user_id}");
                        Console.WriteLine("========================================");
                    }
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("========================================");
                    Console.WriteLine("  [警告] 未能连接到 NapCatQQ");
                    Console.WriteLine("========================================");
                    Console.WriteLine();
                    Console.WriteLine("  请检查:");
                    Console.WriteLine($"  1. NapCatQQ 是否已启动？(双击 napiLoader_debug.bat)");
                    Console.WriteLine("  2. QQ 是否已扫码登录？");
                    Console.WriteLine($"  3. NapCat 的 WebSocket 是否已开启？(检查 config/onebot11.json)");
                    Console.WriteLine($"  4. 端口是否一致？(目标端口: {port})");
                    Console.WriteLine();
                    Console.WriteLine("  ruri-bot 将继续运行，连接建立后自动恢复。");
                }

                Console.WriteLine();
                Console.WriteLine("Bot 已启动！等待消息中...");
                Console.WriteLine("按 Ctrl+C 退出");
                Console.WriteLine();

                // 保持运行
                await Task.Delay(-1);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("  启动失败!");
                Console.WriteLine("========================================");
                Console.WriteLine($"  错误: {ex.Message}");
                Console.WriteLine($"  类型: {ex.GetType().FullName}");
                Console.WriteLine($"  来源: {ex.Source}");
                Console.WriteLine("--- 堆栈跟踪 ---");
                Console.WriteLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("--- 内部异常 ---");
                    Console.WriteLine($"  {ex.InnerException.Message}");
                    Console.WriteLine(ex.InnerException.StackTrace);
                }
                Console.WriteLine("========================================");
                Console.WriteLine("\n按 Enter 退出...");
                Console.ReadLine();
            }
            finally
            {
                core = null;
            }
        }
    }

    /// <summary>
    /// 简单的控制台日志实现
    /// </summary>
    class ConsoleLogger : IRRBotLogger
    {
        public void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void Log(string message, CqMessageChain chain)
        {
            Log($"{message} | CQ: {chain.ToCqQuery()}");
        }
    }
}
