using System;
using System.Collections.Generic;
using Argmax.Sim;
using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>
    /// StarCraft hands on a programmable swarm. Right-click = MOVE (manual override: walk, ignore the program).
    /// A + click = ATTACK (set the order target, let the program decide how to get there and what to fight).
    /// M merges selected equals (2 + 2 = 4), X splits, S stops, H holds, B places the beacon your programs can
    /// follow, Q..Y pick the production slot, Shift+Q..Y reprogram the selection, Ctrl+digit binds a group.
    /// </summary>
    public sealed class CommandInput
    {
        public enum Cursor { None, Attack, Beacon }
        public Cursor Mode { get; private set; }
        public bool Dragging { get; private set; }
        public Vector2 DragFrom, DragTo;
        public Func<bool> PointerOverUi = () => false;
        public Func<bool> KeyboardCaptured = () => false;
        public Action<string> Feedback;
        public Action ToggleEditor, ToggleInspector, Escape;
        readonly Dictionary<int, List<uint>> groups = new Dictionary<int, List<uint>>();
        static readonly KeyCode[] SlotKeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y };
        float lastClick;

        public void Update(ISession s, CameraRig rig, WorldView view)
        {
            var w = s?.World;
            if (w == null) { Mode = Cursor.None; Dragging = false; return; }
            int seat = s.LocalSeat;
            var mouse = Input.mousePosition;
            bool overUi = PointerOverUi();
            view.Hovered = overUi ? 0u : Pick(view, rig.Cam, mouse, -1);

            if (!KeyboardCaptured()) Keys(s, w, seat, rig, view);

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                if (Mode == Cursor.Attack && rig.Ground(mouse, out var hit)) { Order(s, view, CmdKind.Attack, hit); if (!Input.GetKey(KeyCode.LeftShift)) Mode = Cursor.None; return; }
                if (Mode == Cursor.Beacon && rig.Ground(mouse, out var bh))
                {
                    s.Issue(new Cmd { Seat = (byte)seat, Kind = CmdKind.Beacon, X = (short)Mathf.FloorToInt(bh.x), Y = (short)Mathf.FloorToInt(bh.z) });
                    Feedback?.Invoke("beacon placed: programs that `seek beacon` now flow toward it");
                    Mode = Cursor.None;
                    return;
                }
                Dragging = true;
                DragFrom = DragTo = mouse;
            }
            if (Dragging) DragTo = mouse;
            if (Dragging && Input.GetMouseButtonUp(0))
            {
                Dragging = false;
                bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!add) view.Selected.Clear();
                if ((DragTo - DragFrom).sqrMagnitude < 36f)
                {
                    uint id = Pick(view, rig.Cam, mouse, seat);
                    bool dbl = Time.unscaledTime - lastClick < 0.3f;
                    lastClick = Time.unscaledTime;
                    if (id != 0)
                    {
                        if (dbl) SelectSameProgram(w, view, id, rig.Cam);
                        else if (add && view.Selected.Contains(id)) view.Selected.Remove(id);
                        else view.Selected.Add(id);
                    }
                }
                else
                {
                    var r = Rect.MinMaxRect(Mathf.Min(DragFrom.x, DragTo.x), Mathf.Min(DragFrom.y, DragTo.y), Mathf.Max(DragFrom.x, DragTo.x), Mathf.Max(DragFrom.y, DragTo.y));
                    foreach (var d in view.Visible)
                    {
                        if (d.Seat != seat) continue;
                        var sp = rig.Cam.WorldToScreenPoint(d.Pos);
                        if (sp.z > 0 && r.Contains(new Vector2(sp.x, sp.y))) view.Selected.Add(d.Id);
                    }
                }
            }
            if (Input.GetMouseButtonDown(1) && !overUi)
            {
                if (Mode != Cursor.None) { Mode = Cursor.None; return; }
                if (rig.Ground(mouse, out var hit)) Order(s, view, CmdKind.Move, hit);
            }
        }

        void Keys(ISession s, World w, int seat, CameraRig rig, WorldView view)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand);
            if (Input.GetKeyDown(KeyCode.Escape)) { if (Mode != Cursor.None) Mode = Cursor.None; else Escape?.Invoke(); }
            if (Input.GetKeyDown(KeyCode.Tab)) ToggleEditor?.Invoke();
            if (Input.GetKeyDown(KeyCode.I)) ToggleInspector?.Invoke();
            if (Input.GetKeyDown(KeyCode.F)) { view.FieldMode = (view.FieldMode + 1) % WorldView.FieldNames.Length; Feedback?.Invoke("field view: " + WorldView.FieldNames[view.FieldMode]); }
            if (Input.GetKeyDown(KeyCode.Space) && seat < 2 && w.Map.HasCore[seat]) rig.Focus = new Vector3(w.Map.CoreX[seat] + 0.5f, 0, w.Map.CoreY[seat] + 0.5f);
            if (Input.GetKeyDown(KeyCode.P)) s.Paused = !s.Paused;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) s.Speed = Mathf.Min(64f, s.Speed * 2f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) s.Speed = Mathf.Max(0.25f, s.Speed * 0.5f);
            if (!s.CanCommand) return;

            if (Input.GetKeyDown(KeyCode.A) && view.Selected.Count > 0) Mode = Cursor.Attack;
            if (Input.GetKeyDown(KeyCode.B)) Mode = Cursor.Beacon;
            if (Input.GetKeyDown(KeyCode.C)) s.Issue(new Cmd { Seat = (byte)seat, Kind = CmdKind.ClearBeacon });
            if (Input.GetKeyDown(KeyCode.S)) Unit(s, view, CmdKind.Stop);
            if (Input.GetKeyDown(KeyCode.H)) Unit(s, view, CmdKind.Hold);
            if (Input.GetKeyDown(KeyCode.M)) { Unit(s, view, CmdKind.Merge); Feedback?.Invoke("merge: equal values that touch will fuse"); }
            if (Input.GetKeyDown(KeyCode.X)) Unit(s, view, CmdKind.Split);
            if (Input.GetKeyDown(KeyCode.U)) s.Issue(new Cmd { Seat = (byte)seat, Kind = CmdKind.Auto, Arg = w.Seats[seat].Auto ? 0u : 1u });
            if (Input.GetKeyDown(KeyCode.Z)) s.Issue(new Cmd { Seat = (byte)seat, Kind = CmdKind.Spawn, Arg = shift ? 5u : 1u });
            for (int k = 0; k < SlotKeys.Length; k++)
            {
                if (!Input.GetKeyDown(SlotKeys[k])) continue;
                if (shift && view.Selected.Count > 0)
                {
                    uint prog = w.Seats[seat].Loadout[k];
                    if (prog != 0) { Unit(s, view, CmdKind.Reprogram, prog); Feedback?.Invoke("reprogrammed " + view.Selected.Count + " motes to " + (w.GetProgram(prog)?.Name ?? "?")); }
                }
                else s.Issue(new Cmd { Seat = (byte)seat, Kind = CmdKind.Produce, Arg = (uint)k });
            }
            for (int d = 1; d <= 9; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + d)) continue;
                if (ctrl) { groups[d] = new List<uint>(view.Selected); Feedback?.Invoke("group " + d + " = " + view.Selected.Count + " motes"); }
                else if (groups.TryGetValue(d, out var g))
                {
                    if (!shift) view.Selected.Clear();
                    foreach (var id in g) if (w.FindMote(id) >= 0) view.Selected.Add(id);
                }
            }
        }

        void Order(ISession s, WorldView view, CmdKind kind, Vector3 hit)
        {
            if (view.Selected.Count == 0) return;
            s.Issue(new Cmd { Kind = kind, X = (short)Mathf.FloorToInt(hit.x), Y = (short)Mathf.FloorToInt(hit.z), Ids = Ids(view) });
            view.PingOrder(hit, kind == CmdKind.Attack);
        }

        void Unit(ISession s, WorldView view, CmdKind kind, uint arg = 0)
        {
            if (view.Selected.Count == 0) return;
            s.Issue(new Cmd { Kind = kind, Arg = arg, Ids = Ids(view) });
        }

        static uint[] Ids(WorldView view)
        {
            var ids = new uint[view.Selected.Count];
            view.Selected.CopyTo(ids);
            return ids;
        }

        static uint Pick(WorldView view, Camera cam, Vector3 mouse, int seat)
        {
            uint best = 0;
            float bd = 28f * 28f;
            foreach (var d in view.Visible)
            {
                if (seat >= 0 && d.Seat != seat) continue;
                var sp = cam.WorldToScreenPoint(d.Pos);
                if (sp.z <= 0) continue;
                float dd = ((Vector2)sp - (Vector2)mouse).sqrMagnitude;
                if (dd < bd) { bd = dd; best = d.Id; }
            }
            return best;
        }

        static void SelectSameProgram(World w, WorldView view, uint id, Camera cam)
        {
            int i = w.FindMote(id);
            if (i < 0) return;
            uint prog = w.Motes[i].Program;
            byte seat = w.Motes[i].Seat;
            foreach (var d in view.Visible)
            {
                int j = w.FindMote(d.Id);
                if (j < 0 || w.Motes[j].Seat != seat || w.Motes[j].Program != prog) continue;
                var vp = cam.WorldToViewportPoint(d.Pos);
                if (vp.z > 0 && vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1) view.Selected.Add(d.Id);
            }
        }
    }
}
