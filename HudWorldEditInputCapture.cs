using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.ServerMods.WorldEdit;

#nullable disable

namespace VSCreativeMod;

public class HudWorldEditInputCapture : HudElement
{
    private readonly WorldEditClientHandler _handler;
    private readonly WorldEdit _we;
    private readonly HotKey _toolSelectHotkey;
    private readonly ActionConsumable<KeyCombination> _handlerToolSelect;

    public HudWorldEditInputCapture(ICoreClientAPI capi, WorldEditClientHandler worldEditClientHandler) : base(capi)
    {
        _we = capi.ModLoader.GetModSystem<WorldEdit>();
        _handler = worldEditClientHandler;
        _toolSelectHotkey = capi.Input.GetHotKeyByCode("toolmodeselect");
        _handlerToolSelect = _toolSelectHotkey.Handler;
    }

    public bool Toogle(KeyCombination t1)
    {
        if (_handler?.ownWorkspace?.ToolInstance?.ScrollEnabled == true)
        {
            if (_handler.toolModeSelect?.IsOpened() == true)
            {
                return _handler.toolModeSelect.TryClose();
            }

            return _handler.toolModeSelect?.TryOpen(true) ?? false;
        }

        return _handlerToolSelect(t1);
    }

    public override bool TryOpen(bool withFocus)
    {
        _toolSelectHotkey.Handler = Toogle;
        return base.TryOpen(withFocus);
    }

    public override void OnGuiClosed()
    {
        _toolSelectHotkey.Handler = _handlerToolSelect;
        base.OnGuiClosed();
    }

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        base.OnMouseWheel(args);
        if (args.IsHandled) return;
        var toolInstance = _handler?.ownWorkspace?.ToolInstance;
        if (toolInstance?.ScrollEnabled != true || !capi.Input.IsHotKeyPressed("ctrl")) return;

        if (args.DeltaYInt == 0) return;

        var workspace = _we.clientHandler.ownWorkspace;

        var blockFacing = workspace.GetFacing(capi.World.Player.Entity.Pos);
        var facing = blockFacing.Code[0];

        workspace.IntValues.TryGetValue("std.stepSize", out var amount);

        amount *= Math.Sign(args.DeltaYInt);
        switch (toolInstance.ScrollMode)
        {
            case EnumWeToolMode.Move:
            {
                switch (toolInstance)
                {
                    case SelectTool:
                        capi.SendChatMessage($"/we shift {facing} {amount} true");
                        break;
                    case MoveTool:
                    case ImportTool:
                        capi.SendChatMessage($"/we move {facing} {amount} true");
                        break;
                }

                args.SetHandled();
                break;
            }

            case EnumWeToolMode.MoveNear when toolInstance is SelectTool:
                capi.SendChatMessage($"/we g {blockFacing.Opposite.Code[0]} {-amount} true");
                args.SetHandled();
                break;
            case EnumWeToolMode.MoveFar when toolInstance is SelectTool:
                capi.SendChatMessage($"/we g {facing} {amount} true");
                args.SetHandled();
                break;
            case EnumWeToolMode.Rotate:
                capi.SendChatMessage($"/we imr {(args.DeltaY > 0 ? 90 : 270)}");
                args.SetHandled();
                break;
        }
    }
}
