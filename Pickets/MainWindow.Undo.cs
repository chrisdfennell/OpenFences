using System;
using System.IO;
using System.Linq;
using Pickets.Services;

namespace Pickets
{
    // Ctrl+Z for organizing: moving items between fences and tabs, rearranging, removing,
    // deleting fences. See Services/UndoHistory for what is and isn't covered.
    public partial class MainWindow
    {
        private readonly UndoHistory _undo = new();
        private int _undoDepth;

        /// <summary>Record an undo step named <paramref name="description"/>, then run the action.
        /// Actions made of smaller undoable ones (Auto-Import moves many items) record one step.</summary>
        private void Undoable(string description, Action action)
        {
            if (_undoDepth == 0) _undo.Record(description, _fences);
            _undoDepth++;
            try { action(); }
            finally { _undoDepth--; }
        }

        private void Undo()
        {
            // The fence Ctrl+Z was pressed in says what happened, for screen readers.
            var active = _openWindows.FirstOrDefault(w => w.IsActive);
            var result = _undo.Undo(_fences, p => File.Exists(p) || Directory.Exists(p));
            if (result == null)
            {
                System.Media.SystemSounds.Beep.Play();
                active?.Announce("Nothing to undo");
                return;
            }

            foreach (var m in result.Removed)
                _openWindows.FirstOrDefault(w => w.Model == m)?.Close();
            foreach (var m in result.Added)
            {
                var win = new FenceWindow(m);
                HookFenceWindow(win, m);
                _openWindows.Add(win);
                if (!m.Closed) win.Show();
            }
            foreach (var m in result.Changed)
                _openWindows.FirstOrDefault(w => w.Model == m)?.ReloadAfterUndo();

            PlaceUnownedItems(); // anything the restored fences don't place (e.g. new since)
            SaveConfig();

            if (active != null && _openWindows.Contains(active))
            {
                active.FocusCurrentTile();
                active.Announce("Undone: " + result.Description);
            }
        }

        /// <summary>Tray menu text for the next undo.</summary>
        private string UndoMenuText => _undo.NextDescription is string d ? $"Undo: {d}" : "Undo";
    }
}
