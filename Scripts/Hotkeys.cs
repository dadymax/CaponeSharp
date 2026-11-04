using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The original's key shortcuts (window key handler sub_410EC3, letters either case): they press panel buttons
/// (button objects at +20h + 78h × slot). Recruit slots 6–14: T Thug, P Pistol, M Thompson, B Terrorist, S Sniper,
/// L Shotgun, K Hit man, C Cadillac, I Thief (the pressed one of a unit in training cancels it). Unit orders: G Go
/// (slot 0), A Attack (1), E Extort (2), D Disguise (4), O Patrol (5). H centres the view on the player's HQ. R repairs
/// the selected own company (its slot 5). Ctrl + digit stores the selected own units (not collectors) as a group; the
/// digit selects it again, and once selected moves the view to it; Shift + digit selects and moves the view at once.
/// Ctrl + G / A / I open the money, alliances and statistics windows. Pause (remake, user request): freezes the game.
/// </summary>
public partial class Hotkeys : Node
{
    private static readonly Dictionary<Key, int> RecruitSlots = new()
    {
        [Key.T] = 6, [Key.P] = 7, [Key.M] = 8, [Key.B] = 9, [Key.S] = 10, [Key.L] = 11, [Key.K] = 12, [Key.C] = 13, [Key.I] = 14,
    };

    private static readonly Dictionary<Key, int> OrderSlots = new()
    {
        [Key.G] = 0, [Key.A] = 1, [Key.E] = 2, [Key.D] = 4, [Key.O] = 5,
    };

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || key.AltPressed)
            return;
        if (Main.InputBlocked)
            return;
        // Pause: by key code — the real Pause key has no usable physical code on Windows (user: "pause not
        // work at all"; a synthetic test event had both codes and passed).
        if (IsPauseKey(key))
        {
            if (Main.Instance?.TogglePause() == true)
                GetViewport().SetInputAsHandled();
            return;
        }
        // F8: quick load.
        if (key.Keycode == Key.F8)
        {
            if (Main.Instance?.QuickLoad() == true)
                GetViewport().SetInputAsHandled();
            return;
        }
        // F5: quick save (TODO item 20; one slot, no menu).
        if (key.Keycode == Key.F5)
        {
            if (Main.Instance?.QuickSave() == true)
                GetViewport().SetInputAsHandled();
            return;
        }
        // Physical keys: the same places on a Russian layout.
        var code = key.PhysicalKeycode;
        if (code is >= Key.Key0 and <= Key.Key9)
        {
            var n = code - Key.Key0;
            var main = Main.Instance;
            if (main != null && (key.CtrlPressed ? main.StoreGroup((int)n) : main.RecallGroup((int)n, key.ShiftPressed)))
                GetViewport().SetInputAsHandled();
            return;
        }
        if (key.CtrlPressed)
        {
            // sub_410EC3 with Ctrl (GetKeyState 11h): G money window (sub_4629A6), A alliances (sub_45B796),
            // I statistics (sub_45FD50); M opens the multiplayer message window (MessSend, sub_45D146) — not ported.
            var window = code switch
            {
                Key.G => Main.Instance?.ShowMoneyWindow() != null,
                Key.A => Main.Instance?.ShowAllianceWindow() != null,
                Key.I => Main.Instance?.ShowRankWindow() != null,
                _ => false,
            };
            if (window)
                GetViewport().SetInputAsHandled();
            return;
        }
        var handled = code switch
        {
            _ when RecruitSlots.TryGetValue(code, out var slot) => RecruitPanel.Instance?.Press(slot) == true,
            _ when OrderSlots.TryGetValue(code, out var slot) => ActionBar.Instance?.Press(slot) == true,
            Key.H => CenterOnHq(),
            // R: an own idle company's slot 5 — Repair (company vt+0BCh with the button's action).
            Key.R => GuiGameback.Instance?.SelectedNode is Company { } c && c.OwnerTeam?.IsCurrentPlayerColor == true
                     && ActionBar.Instance?.Press(5) == true,
            _ => false,
        };
        if (handled)
            GetViewport().SetInputAsHandled();
    }

    public static bool IsPauseKey(InputEventKey key) =>
        key.Keycode == Key.Pause || key.PhysicalKeycode == Key.Pause;

    /// <summary>H: the view goes to the player's HQ (sub_409FE7).</summary>
    private static bool CenterOnHq()
    {
        var hq = Teams.GetCurrentPlayerTeam()?.HqCompany;
        if (hq == null || !IsInstanceValid(hq))
            return false;
        Minimap.CenterView(hq.GlobalPosition);
        return true;
    }
}
