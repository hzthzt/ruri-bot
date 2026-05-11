using System;
using NapCatSharpLib.Data;
using NapCatSharpLib.Enum;
using NapCatSharpLib.Message;
using RuriBot.Library.Data;
using RuriBot.Library.Module;

namespace RuriBot.DemoModule
{
    /// <summary>
    /// 演示模块 —— 用于验证 ruri-bot 框架是否正常工作
    ///
    /// 支持的群聊命令:
    ///   /ping          - 测试连通性，返回 pong
    ///   /echo <text>   - 复读消息
    ///   /about         - 显示模块信息
    ///   /help          - 显示帮助
    ///
    /// 事件监听:
    ///   - 群成员增加时发送欢迎消息
    /// </summary>
    public class DemoModule : RRBotModuleBase
    {
        protected override void SetModuleInfo()
        {
            module_id = "demo";
            module_name = "演示模块";
            module_version = "1.0.0";
            module_author = "ruri-bot";
        }

        // ========== 命令注册 ==========

        protected override void RegisterPrivateCommands()
        {
            var perm = ModulePermissionInterface;

            cmdRegister.RegisterPrivate("ping", perm, OnPingPrivate);
            cmdRegister.RegisterPrivate("echo", perm, OnEchoPrivate);
            cmdRegister.RegisterPrivate("about", perm, OnAboutPrivate);
            cmdRegister.RegisterPrivate("help", perm, OnHelpPrivate);
        }

        protected override void UnregisterPrivateCommands()
        {
            var perm = ModulePermissionInterface;

            cmdRegister.UnregisterPrivate("ping", perm, OnPingPrivate);
            cmdRegister.UnregisterPrivate("echo", perm, OnEchoPrivate);
            cmdRegister.UnregisterPrivate("about", perm, OnAboutPrivate);
            cmdRegister.UnregisterPrivate("help", perm, OnHelpPrivate);
        }

        protected override void RegisterGroupCommands()
        {
            var perm = ModulePermissionInterface;

            cmdRegister.RegisterGroup("ping", perm, OnPingGroup);
            cmdRegister.RegisterGroup("echo", perm, OnEchoGroup);
            cmdRegister.RegisterGroup("about", perm, OnAboutGroup);
            cmdRegister.RegisterGroup("help", perm, OnHelpGroup);
        }

        protected override void UnregisterGroupCommands()
        {
            var perm = ModulePermissionInterface;

            cmdRegister.UnregisterGroup("ping", perm, OnPingGroup);
            cmdRegister.UnregisterGroup("echo", perm, OnEchoGroup);
            cmdRegister.UnregisterGroup("about", perm, OnAboutGroup);
            cmdRegister.UnregisterGroup("help", perm, OnHelpGroup);
        }

        // ========== 事件注册 ==========

        protected override void RegisterNapCatEvents()
        {
            var perm = ModulePermissionInterface;

            // 监听所有消息并输出到控制台
            evtRegister.Register<NapCatMessagePrivate>(perm, OnMessagePrivate);
            evtRegister.Register<NapCatMessageGroup>(perm, OnMessageGroup);

            // 监听群成员增加事件
            evtRegister.Register<NapCatNoticeGroupIncrease>(
                perm, OnGroupMemberJoin);

            // 监听群成员减少事件
            evtRegister.Register<NapCatNoticeGroupDecrease>(
                perm, OnGroupMemberLeave);
        }

        protected override void UnregisterNapCatEvents()
        {
            var perm = ModulePermissionInterface;

            evtRegister.Unregister<NapCatMessagePrivate>(perm, OnMessagePrivate);
            evtRegister.Unregister<NapCatMessageGroup>(perm, OnMessageGroup);

            evtRegister.Unregister<NapCatNoticeGroupIncrease>(
                perm, OnGroupMemberJoin);

            evtRegister.Unregister<NapCatNoticeGroupDecrease>(
                perm, OnGroupMemberLeave);
        }

        // ========== 命令处理器 ==========

        private async void OnPingPrivate(RRBotCommand cmd, NapCatMessagePrivate msg)
        {
            var reply = new CqMessageChain();
            reply.Builder.AddText("pong!\n");
            reply.Builder.AddText($"延迟: {DateTimeOffset.Now.ToUnixTimeMilliseconds() - msg.time * 1000}ms");
            await api.SendPrivateMessage(msg.user_id, reply);
        }

        private async void OnPingGroup(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var reply = new CqMessageChain();
            reply.Builder.AddReply(msg.message_id);
            reply.Builder.AddText("pong!");
            await api.SendGroupMessage(msg.group_id, reply);
        }

        private async void OnEchoPrivate(RRBotCommand cmd, NapCatMessagePrivate msg)
        {
            var reply = new CqMessageChain();
            if (cmd.CommandArgsCount > 0)
            {
                reply.Builder.AddText(string.Join(" ", cmd.CommandArgs));
            }
            else
            {
                reply.Builder.AddText("用法: /echo <要复读的内容>");
            }
            await api.SendPrivateMessage(msg.user_id, reply);
        }

        private async void OnEchoGroup(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var reply = new CqMessageChain();
            reply.Builder.AddReply(msg.message_id);

            if (cmd.CommandArgsCount > 0)
            {
                reply.Builder.AddText(string.Join(" ", cmd.CommandArgs));
            }
            else
            {
                reply.Builder.AddText("用法: /echo <要复读的内容>");
            }
            await api.SendGroupMessage(msg.group_id, reply);
        }

        private async void OnAboutPrivate(RRBotCommand cmd, NapCatMessagePrivate msg)
        {
            var reply = BuildAboutMessage();
            await api.SendPrivateMessage(msg.user_id, reply);
        }

        private async void OnAboutGroup(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var reply = new CqMessageChain();
            reply.Builder.AddReply(msg.message_id);
            reply.Builder.AddText("ruri-bot Demo 模块 v1.0.0\n");
            reply.Builder.AddText("基于 NapCatSharpLib + ruri-bot 框架\n");
            reply.Builder.AddText("命令: /ping /echo /about /help");
            await api.SendGroupMessage(msg.group_id, reply);
        }

        private CqMessageChain BuildAboutMessage()
        {
            var chain = new CqMessageChain();
            chain.Builder.AddText("=== ruri-bot Demo 模块 ===\n");
            chain.Builder.AddText($"版本: {module_version}\n");
            chain.Builder.AddText($"作者: {module_author}\n");
            chain.Builder.AddText("框架: NapCatSharpLib + ruri-bot\n");
            chain.Builder.AddText("命令: /ping /echo /about /help");
            return chain;
        }

        private async void OnHelpPrivate(RRBotCommand cmd, NapCatMessagePrivate msg)
        {
            var reply = BuildHelpMessage();
            await api.SendPrivateMessage(msg.user_id, reply);
        }

        private async void OnHelpGroup(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var reply = BuildHelpMessage();
            await api.SendGroupMessage(msg.group_id, reply);
        }

        private CqMessageChain BuildHelpMessage()
        {
            var chain = new CqMessageChain();
            chain.Builder.AddText("=== 可用命令 ===\n");
            chain.Builder.AddText("/ping        - 测试连通性\n");
            chain.Builder.AddText("/echo <内容> - 复读消息\n");
            chain.Builder.AddText("/about       - 关于信息\n");
            chain.Builder.AddText("/help        - 显示此帮助");
            return chain;
        }

        // ========== 事件处理器 ==========

        private void OnMessagePrivate(NapCatMessagePrivate msg)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(msg.time).LocalDateTime;
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════");
            Console.WriteLine($"  [私聊消息] {dt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("───────────────────────────────────────");
            Console.WriteLine($"  消息ID : {msg.message_id}");
            Console.WriteLine($"  发送者 : {msg.sender.nickname} (QQ: {msg.user_id})");
            Console.WriteLine($"  性别   : {msg.sender.sex}");
            Console.WriteLine($"  年龄   : {msg.sender.age}");
            Console.WriteLine($"  子类型 : {msg.sub_type}");
            Console.WriteLine("───────────────────────────────────────");
            Console.WriteLine($"  内容   : {msg.raw_message}");
            if (msg.raw_message != msg.message)
                Console.WriteLine($"  原始CQ : {msg.message}");
            Console.WriteLine("═══════════════════════════════════════");
        }

        private void OnMessageGroup(NapCatMessageGroup msg)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(msg.time).LocalDateTime;
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════");
            Console.WriteLine($"  [群聊消息] {dt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("───────────────────────────────────────");
            Console.WriteLine($"  消息ID : {msg.message_id}");
            Console.WriteLine($"  群号   : {msg.group_id}");
            Console.WriteLine($"  发送者 : {msg.sender.nickname}");
            Console.WriteLine($"  群名片 : {msg.sender.card}");
            Console.WriteLine($"  QQ号   : {msg.sender.user_id}");
            Console.WriteLine($"  角色   : {msg.sender.role}");
            Console.WriteLine($"  子类型 : {msg.sub_type}");
            if (msg.anonymous != null)
                Console.WriteLine($"  [匿名] : {msg.anonymous.name} (flag: {msg.anonymous.flag})");
            Console.WriteLine("───────────────────────────────────────");
            Console.WriteLine($"  内容   : {msg.raw_message}");
            if (msg.raw_message != msg.message)
                Console.WriteLine($"  原始CQ : {msg.message}");
            Console.WriteLine("═══════════════════════════════════════");
        }

        private async void OnGroupMemberJoin(NapCatNoticeGroupIncrease notice)
        {
            var reply = new CqMessageChain();

            // 判断是主动加入还是被邀请
            if (notice.sub_type == NapCatNoticeGroupIncreaseSubType.approve)
            {
                reply.Builder.AddText("欢迎新成员加入本群！");
            }
            else if (notice.sub_type == NapCatNoticeGroupIncreaseSubType.invite)
            {
                reply.Builder.AddText($"欢迎新成员！(由 {notice.operator_id} 邀请)");
            }

            await api.SendGroupMessage(notice.group_id, reply);
        }

        private async void OnGroupMemberLeave(NapCatNoticeGroupDecrease notice)
        {
            var reply = new CqMessageChain();

            if (notice.sub_type == NapCatNoticeGroupDecreaseSubType.leave)
            {
                reply.Builder.AddText($"成员 {notice.user_id} 离开了群聊");
            }
            else if (notice.sub_type == NapCatNoticeGroupDecreaseSubType.kick)
            {
                reply.Builder.AddText($"成员 {notice.user_id} 被移出了群聊");
            }
            else if (notice.sub_type == NapCatNoticeGroupDecreaseSubType.kick_me)
            {
                reply.Builder.AddText("机器人被移出了群聊");
            }

            await api.SendGroupMessage(notice.group_id, reply);
        }
    }
}
