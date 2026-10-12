using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.Text.RegularExpressions;

namespace Financements.LegacyPilot;

internal static class EmpruntPage
{
    public static void OpenList(Window main)
    {
        if (!main.IsEnabled) throw new DesktopBlockedException("Une fenêtre modale bloque la navigation.");
        EnsureVisible(main);
        // Observed on the installed Financements application connected to QA_FIN.
        var tabs = main.FindAllChildren(cf => cf.ByAutomationId("1000").And(cf.ByControlType(ControlType.Tab)));
        if (tabs.Length != 1) throw new DesktopBlockedException("Barre d'onglets de navigation absente ou ambiguë.");
        var items = tabs[0].FindAllChildren(cf => cf.ByName("Liste complète").And(cf.ByControlType(ControlType.TabItem)));
        if (items.Length == 0)
        {
            var navigator = Unique(main.FindAllChildren(cf => cf.ByName("Navigateur").And(cf.ByControlType(ControlType.Pane))), "Navigateur");
            var tree = Unique(navigator.FindAllChildren(cf => cf.ByControlType(ControlType.Tree)), "Arbre de navigation");
            var root = Unique(tree.FindAllChildren(cf => cf.ByName("Financements").And(cf.ByControlType(ControlType.TreeItem))), "Financements");
            root.AsTreeItem().Expand();
            var dette = Unique(root.FindAllChildren(cf => cf.ByName("Dette").And(cf.ByControlType(ControlType.TreeItem))), "Dette");
            dette.AsTreeItem().Expand();
            var emprunts = Unique(dette.FindAllChildren(cf => cf.ByName("Emprunts").And(cf.ByControlType(ControlType.TreeItem))), "Emprunts");
            emprunts.AsTreeItem().Expand();
            var lists = emprunts.FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem));
            var complete = lists.Where(e => e.Name == "Liste complète").ToArray();
            if (complete.Length != 1)
                throw new DesktopBlockedException("Liste complète absente ou ambiguë dans Emprunts. Entrées observées : " + string.Join(" ; ", lists.Select(e => e.Name)));
            main.Focus();
            if (complete[0].Patterns.ScrollItem.IsSupported)
                complete[0].Patterns.ScrollItem.Pattern.ScrollIntoView();
            if (!complete[0].IsEnabled || complete[0].IsOffscreen)
                throw new DesktopBlockedException("L'entrée Liste complète n'est pas visible ou activable.");
            complete[0].AsTreeItem().Select();
            complete[0].DoubleClick(); // MainTree.OnDblclk dispatches ACTION_OUVRIR for the selected list.
        }
        else
        {
            if (items.Length != 1 || !items[0].Patterns.SelectionItem.IsSupported)
                throw new DesktopBlockedException("L'onglet observé Liste complète est ambigu ou non sélectionnable.");
            items[0].Patterns.SelectionItem.Pattern.Select();
        }
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 10)
        {
            if (!main.IsEnabled) throw new DesktopBlockedException("Une fenêtre modale bloque l'ouverture de la liste.");
            var activeItems = tabs[0].FindAllChildren(cf => cf.ByName("Liste complète").And(cf.ByControlType(ControlType.TabItem)));
            if (main.Title.EndsWith("[ Liste complète]", StringComparison.Ordinal) && activeItems.Length == 1 && activeItems[0].Patterns.SelectionItem.Pattern.IsSelected.Value)
                return;
            Thread.Sleep(200);
        }
        throw new DesktopBlockedException("L'activation de Liste complète n'a pas été confirmée.");
    }

    public static void SearchByNumber(Window main, int number, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var list = ActiveList(main);
        var edits = list.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .Where(e => e.IsEnabled && !e.IsOffscreen).ToArray();
        var named = edits.Where(e => Regex.IsMatch(e.Name ?? "", "recherche|search", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        var search = named.Length == 1 ? named[0] : edits.Length == 1 ? edits[0] : null;
        if (search == null)
            throw new DesktopBlockedException("Champ de recherche de Liste complète absent ou ambigu.");
        search.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Keyboard.Type(VirtualKeyShort.RETURN);
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(15))
        {
            cancellation.ThrowIfCancellationRequested();
            if (ExactNumberCells(ActiveList(main), number).Length == 1) return;
            if (cancellation.WaitHandle.WaitOne(200)) cancellation.ThrowIfCancellationRequested();
        }
        throw new DesktopBlockedException($"Aucune ligne unique avec le N° fiche {number} n'a été confirmée dans la liste.");
    }

    public static void OpenEmprunt(Window main, int number, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var cells = ExactNumberCells(ActiveList(main), number);
        if (cells.Length != 1)
            throw new DesktopBlockedException($"La ligne du N° fiche {number} est absente ou ambiguë avant le double-clic.");
        var cell = cells[0];
        if (!cell.IsEnabled || cell.IsOffscreen)
            throw new DesktopBlockedException($"La ligne du N° fiche {number} n'est pas activable.");
        cell.DoubleClick();
    }

    public static void VerifyOpened(Window main, int number, CancellationToken cancellation)
    {
        var titlePattern = new Regex(@"\bEmprunt N°\s*" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(15))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!main.IsEnabled)
            {
                var warnings = main.FindAllDescendants(cf => cf.ByName("Avertissement").And(cf.ByControlType(ControlType.Window)));
                if (warnings.Length == 1 && warnings[0].FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                    .Any(e => (e.Name ?? "").Contains("Cette fiche est déjà en cours d'utilisation.", StringComparison.Ordinal)
                        && (e.Name ?? "").Contains("Elle ne sera donc ouverte qu'en mode consultation.", StringComparison.Ordinal)))
                    throw new DesktopBlockedException($"La fiche N°{number} est déjà en cours d'utilisation ; Financements demande une ouverture en consultation.");
                throw new DesktopBlockedException("Une fenêtre modale bloque la vérification de la fiche emprunt.");
            }
            var tabs = main.FindAllChildren(cf => cf.ByAutomationId("1000").And(cf.ByControlType(ControlType.Tab)));
            if (tabs.Length == 1)
            {
                var matching = tabs[0].FindAllChildren(cf => cf.ByControlType(ControlType.TabItem))
                    .Where(e => titlePattern.IsMatch(e.Name) && e.Patterns.SelectionItem.IsSupported && e.Patterns.SelectionItem.Pattern.IsSelected.Value).ToArray();
                if (matching.Length == 1 && titlePattern.IsMatch(main.Title)) return;
            }
            if (cancellation.WaitHandle.WaitOne(200)) cancellation.ThrowIfCancellationRequested();
        }
        throw new DesktopBlockedException($"La fiche Emprunt N°{number} n'a pas été confirmée dans le titre et l'onglet sélectionné.");
    }

    private static AutomationElement ActiveList(Window main)
    {
        EnsureVisible(main);
        if (!main.IsEnabled || !main.Title.EndsWith("[ Liste complète]", StringComparison.Ordinal))
            throw new DesktopBlockedException("Liste complète non active ou fenêtre modale ouverte.");
        var mdi = Unique(main.FindAllChildren(cf => cf.ByAutomationId("59648")), "Espace de travail");
        return Unique(mdi.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
            .Where(e => e.Name?.Trim() == "Liste complète").ToArray(), "Fenêtre Liste complète");
    }

    private static void EnsureVisible(Window main)
    {
        if (!main.Patterns.Window.IsSupported)
            throw new DesktopBlockedException("L'état de la fenêtre Financements n'est pas accessible.");
        var window = main.Patterns.Window.Pattern;
        if (window.WindowVisualState.Value == WindowVisualState.Minimized)
            window.SetWindowVisualState(WindowVisualState.Normal);
        main.Focus();
    }

    private static AutomationElement[] ExactNumberCells(AutomationElement list, int number)
    {
        string expected = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var headers = list.FindAllDescendants()
            .Where(e => e.Name?.Trim() == "N° fiche" && !e.IsOffscreen).ToArray();
        var headerItems = headers.Where(e => e.ControlType == ControlType.HeaderItem).ToArray();
        if (headerItems.Length > 0) headers = headerItems;
        if (headers.Length != 1)
            throw new DesktopBlockedException("En-tête N° fiche de la liste absent ou ambigu : la colonne du numéro ne peut pas être vérifiée.");
        var column = headers[0].BoundingRectangle;
        var matches = list.FindAllDescendants()
            .Where(e => e.ControlType is ControlType.Text or ControlType.DataItem or ControlType.Custom
                && e.Name?.Trim() == expected && !e.IsOffscreen)
            .Where(e =>
            {
                var bounds = e.BoundingRectangle;
                var center = bounds.Left + bounds.Width / 2;
                return bounds.Width > 0 && center >= column.Left && center <= column.Right;
            }).ToArray();
        var textCells = matches.Where(e => e.ControlType == ControlType.Text).ToArray();
        return (textCells.Length > 0 ? textCells : matches)
            .DistinctBy(e => e.BoundingRectangle).ToArray();
    }

    private static AutomationElement Unique(AutomationElement[] elements, string name) => elements.Length == 1
        ? elements[0] : throw new DesktopBlockedException("Contrôle de navigation absent ou ambigu : " + name);
}
