﻿using AmongUs.GameOptions;
using Hazel;
using System.Collections.Generic;
using System.Linq;
using TOHE.Modules;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Double;
using TOHE.Roles.Impostor;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE.Roles.Neutral;

internal class Pelican : RoleBase
{
    //===========================SETUP================================\\
    private const int Id = 17300;
    private static HashSet<byte> playerIdList = [];
    public static bool HasEnabled => playerIdList.Count > 0;
    public override bool IsEnable => HasEnabled;
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;

    //==================================================================\\

    private static Dictionary<byte, List<byte>> eatenList = [];
    private static readonly Dictionary<byte, float> originalSpeed = [];
    private static int Count = 0;
    public static OptionItem KillCooldown;
    public static OptionItem HasImpostorVision;
    public static OptionItem CanVent;
    public static void SetupCustomOption()
    {
        Options.SetupSingleRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.Pelican, 1, zeroOne: false);
        KillCooldown = FloatOptionItem.Create(Id + 10, "PelicanKillCooldown", new(0f, 180f, 2.5f), 30f, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Pelican])
            .SetValueFormat(OptionFormat.Seconds);
        CanVent = BooleanOptionItem.Create(Id + 11, "CanVent", true, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Pelican]);
        HasImpostorVision = BooleanOptionItem.Create(Id + 12, "ImpostorVision", true, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Pelican]);
    }
    public override void Init()
    {
        playerIdList = [];
        eatenList = [];
        Count = 0;
    }
    public override void Add(byte playerId)
    {
        playerIdList.Add(playerId);

        if (!AmongUsClient.Instance.AmHost) return;
        if (!Main.ResetCamPlayerList.Contains(playerId))
            Main.ResetCamPlayerList.Add(playerId);
    }
    private static void SyncEatenList(byte playerId)
    {
        SendRPC(byte.MaxValue);
        foreach (var el in eatenList)
            SendRPC(el.Key);
    }
    private static void SendRPC(byte playerId)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetPelicanEatenNum, SendOption.Reliable, -1);
        writer.Write(playerId);
        if (playerId != byte.MaxValue)
        {
            writer.Write(eatenList[playerId].Count);
            foreach (var el in eatenList[playerId])
                writer.Write(el);
        }
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public static void ReceiveRPC(MessageReader reader)
    {
        byte playerId = reader.ReadByte();
        if (playerId == byte.MaxValue)
        {
            eatenList.Clear();
        }
        else
        {
            int eatenNum = reader.ReadInt32();
            eatenList.Remove(playerId);
            List<byte> list = [];
            for (int i = 0; i < eatenNum; i++)
                list.Add(reader.ReadByte());
            eatenList.Add(playerId, list);
        }
    }
    public override void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = KillCooldown.GetFloat();
    public override bool CanUseKillButton(PlayerControl pc) => pc.IsAlive();
    public override bool CanUseImpostorVentButton(PlayerControl pc) => Pelican.CanVent.GetBool();
    private static bool IsEaten(PlayerControl pc, byte id) => eatenList.ContainsKey(pc.PlayerId) && eatenList[pc.PlayerId].Contains(id);
    public static bool IsEaten(byte id)
    {
        foreach (var el in eatenList)
            if (el.Value.Contains(id))
                return true;
        return false;
    }
    public static bool CanEat(PlayerControl pc, byte id)
    {
        if (!pc.Is(CustomRoles.Pelican) || GameStates.IsMeeting) return false;

        var target = Utils.GetPlayerById(id);

        if (Penguin.AbductVictim != null)
            if (target.Is(CustomRoles.Penguin) || id == Penguin.AbductVictim.PlayerId)
                return false;

        return target != null && target.CanBeTeleported() && !target.Is(CustomRoles.Pestilence) && !Medic.ProtectList.Contains(target.PlayerId) && !target.Is(CustomRoles.GM) && !IsEaten(pc, id) && !IsEaten(id);
    }
    public static Vector2 GetBlackRoomPSForPelican()
    {
        return Utils.GetActiveMapId() switch
        {
            0 => new Vector2(-27f, 3.3f), // The Skeld
            1 => new Vector2(-11.4f, 8.2f), // MIRA HQ
            2 => new Vector2(42.6f, -19.9f), // Polus
            3 => new Vector2(27f, 3.3f), // dlekS ehT
            4 => new Vector2(-16.8f, -6.2f), // Airship
            5 => new Vector2(9.6f, 23.2f), // The Fungle
            _ => throw new System.NotImplementedException(),
        };
    }
    public override void ApplyGameOptions(IGameOptions opt, byte id) => opt.SetVision(HasImpostorVision.GetBool());

    public override string GetProgressText(byte playerId, bool coooms)
    {
        var player = Utils.GetPlayerById(playerId);
        if (player == null) return "Invalid";
        var eatenNum = 0;
        if (eatenList.ContainsKey(playerId))
            eatenNum = eatenList[playerId].Count;
        return Utils.ColorString(eatenNum < 1 ? Color.gray : Utils.GetRoleColor(CustomRoles.Pelican), $"({eatenNum})");
    }
    private static void EatPlayer(PlayerControl pc, PlayerControl target)
    {
        if (pc == null || target == null || !target.CanBeTeleported()) return;
        if (Mini.Age < 18 && (target.Is(CustomRoles.NiceMini) || target.Is(CustomRoles.EvilMini)))
        {
            pc.Notify(Utils.ColorString(Utils.GetRoleColor(CustomRoles.NiceMini), GetString("CantEat")));
            return;
        }

        if (!eatenList.ContainsKey(pc.PlayerId)) eatenList.Add(pc.PlayerId, []);
        eatenList[pc.PlayerId].Add(target.PlayerId);

        SyncEatenList(pc.PlayerId);

        originalSpeed.Remove(target.PlayerId);
        originalSpeed.Add(target.PlayerId, Main.AllPlayerSpeed[target.PlayerId]);

        target.RpcTeleport(GetBlackRoomPSForPelican());
        Main.AllPlayerSpeed[target.PlayerId] = 0.5f;
        ReportDeadBodyPatch.CanReport[target.PlayerId] = false;
        target.MarkDirtySettings();

        Utils.NotifyRoles(SpecifySeer: pc);
        Utils.NotifyRoles(SpecifySeer: target);

        Logger.Info($"{pc.GetRealName()} eat player => {target.GetRealName()}", "Pelican");
    }

    public override void OnReportDeadBody(PlayerControl Nah_Id, PlayerControl win)
    {
        foreach (var pc in eatenList)
        {
            foreach (var tar in pc.Value)
            {
                var target = Utils.GetPlayerById(tar);
                var killer = Utils.GetPlayerById(pc.Key);
                if (killer == null || target == null) continue;
                Main.AllPlayerSpeed[tar] = Main.AllPlayerSpeed[tar] - 0.5f + originalSpeed[tar];
                ReportDeadBodyPatch.CanReport[tar] = true;
                target.RpcExileV2();
                target.SetRealKiller(killer);
                Main.PlayerStates[tar].deathReason = PlayerState.DeathReason.Eaten;
                Main.PlayerStates[tar].SetDead();
                Utils.AfterPlayerDeathTasks(target, true);
                Logger.Info($"{killer.GetRealName()} 消化了 {target.GetRealName()}", "Pelican");
            }
        }
        eatenList.Clear();
        SyncEatenList(byte.MaxValue);
    }
    public override bool OnCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        if (CanEat(killer, target.PlayerId))
        {
            EatPlayer(killer, target);
            if (!Options.DisableShieldAnimations.GetBool()) killer.RpcGuardAndKill(killer);
            killer.SetKillCooldown();
            killer.RPCPlayCustomSound("Eat");
            target.RPCPlayCustomSound("Eat");
        }
        else
        {
            killer.SetKillCooldown();
            killer.Notify(GetString("Pelican.TargetCannotBeEaten"));
        }
        return false;
    }

    public override void OnTargetDead(PlayerControl SLAT, PlayerControl victim)
    {
        var pc = victim.PlayerId;
        if (!eatenList.ContainsKey(pc)) return;

        foreach (var tar in eatenList[pc])
        {
            var target = Utils.GetPlayerById(tar);
            var player = Utils.GetPlayerById(pc);
            if (player == null || target == null) continue;

            target.RpcTeleport(player.GetCustomPosition());

            Main.AllPlayerSpeed[tar] = Main.AllPlayerSpeed[tar] - 0.5f + originalSpeed[tar];
            ReportDeadBodyPatch.CanReport[tar] = true;
            
            target.MarkDirtySettings();
            
            RPC.PlaySoundRPC(tar, Sounds.TaskComplete);
            
            Logger.Info($"{Utils.GetPlayerById(pc).GetRealName()} dead, player return back: {target.GetRealName()}", "Pelican");
        }
        eatenList.Remove(pc);
        SyncEatenList(pc);
        Utils.NotifyRoles();
    }

    public override void OnFixedUpdate(PlayerControl peli)
    {
        if (!GameStates.IsInTask)
        {
            if (eatenList.Count > 0)
            {
                eatenList.Clear();
                SyncEatenList(byte.MaxValue);
            }
            return;
        }
        
        Count--;
        
        if (Count > 0) return; 
        
        Count = 15;

        foreach (var pc in eatenList)
        {
            foreach (var tar in pc.Value.ToArray())
            {
                var target = Utils.GetPlayerById(tar);
                if (target == null) continue;

                var pos = GetBlackRoomPSForPelican();
                var dis = Vector2.Distance(pos, target.GetCustomPosition());
                if (dis < 1f) continue;

                target.RpcTeleport(pos, sendInfoInLogs: false);
                //Utils.NotifyRoles(SpecifySeer: target, ForceLoop: false);
            }
        }
    }
    public override void SetAbilityButtonText(HudManager hud, byte playerId)
    {
        hud.KillButton.OverrideText(GetString("PelicanButtonText"));

        hud.SabotageButton.ToggleVisible(false);
        hud.AbilityButton.ToggleVisible(false);
        hud.ReportButton.ToggleVisible(false);
    }
    public override Sprite GetKillButtonSprite(PlayerControl player, bool shapeshifting) => CustomButton.Get("Vulture");
}
