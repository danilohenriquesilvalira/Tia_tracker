using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaTracker.Core;
using TiaTracker.Diff;
using TiaTracker.Models;
using Siemens.Engineering.Compare;

namespace TiaTracker.UI
{
    public class ConsoleMenu
    {
        private readonly TiaConnection   _conn;
        private readonly SnapshotManager _snapshots;
        private readonly string          _baseDir;

        public ConsoleMenu(TiaConnection conn, SnapshotManager snapshots, string baseDir)
        {
            _conn      = conn;
            _snapshots = snapshots;
            _baseDir   = baseDir;
        }

        public void Run()
        {
            while (true)
            {
                PrintHeader();
                Console.Write("Opção: ");
                var input = Console.ReadLine()?.Trim();
                Console.WriteLine();

                switch (input)
                {
                    case "1": DoSnapshot();        break;
                    case "2": DoDiff();            break;
                    case "3": DoOnlineCompare();   break;
                    case "4": DoListSnapshots();   break;
                    case "5": DoReadBlocks();      break;
                    case "6": DoCallGraph();       break;
                    case "0":                      return;
                    default:
                        Console.WriteLine("  Opção inválida.\n");
                        break;
                }
            }
        }

        private void PrintHeader()
        {
            Console.WriteLine("┌────────────────────────────────────────────┐");
            Console.WriteLine($"│  Projecto: {_conn.Project.Name,-32}│");
            Console.WriteLine("├────────────────────────────────────────────┤");
            Console.WriteLine("│  [1]  Tirar snapshot (exportar blocos XML) │");
            Console.WriteLine("│  [2]  Ver o que mudou (diff snapshots)     │");
            Console.WriteLine("│  [3]  Comparar Projecto vs PLC Online      │");
            Console.WriteLine("│  [4]  Listar snapshots                     │");
            Console.WriteLine("│  [5]  Ler blocos do projecto (FC/FB/OB)    │");
            Console.WriteLine("│  [6]  Grafo de chamadas                    │");
            Console.WriteLine("│  [0]  Sair                                 │");
            Console.WriteLine("└────────────────────────────────────────────┘");
        }

        // ── Snapshot ──────────────────────────────────────────────────────────

        private void DoSnapshot()
        {
            Console.WriteLine("  A exportar todos os blocos...\n");

            var folder   = _snapshots.CreateSnapshotFolder(_conn.Project.Name);
            var exporter = new BlockExporter(_conn.Project);
            var stats    = exporter.ExportAll(folder);

            var info = new SnapshotInfo
            {
                ProjectName = _conn.Project.Name,
                Timestamp   = DateTime.Now,
                FolderPath  = folder,
                TotalBlocks = stats.Blocks,
                TotalTagTables = stats.TagTables
            };
            _snapshots.SaveIndex(folder, info);

            Console.WriteLine();
            Console.WriteLine($"  Snapshot completo!");
            Console.WriteLine($"  Blocos exportados : {stats.Blocks}");
            Console.WriteLine($"  Tag Tables        : {stats.TagTables}");
            if (stats.Errors.Count > 0)
                Console.WriteLine($"  Avisos            : {stats.Errors.Count} blocos ignorados");
            Console.WriteLine($"  Pasta             : {folder}");
            Console.WriteLine();
        }

        // ── Diff ──────────────────────────────────────────────────────────────

        private void DoDiff()
        {
            var (older, newer) = _snapshots.GetLastTwo(_conn.Project.Name);

            if (older == null)
            {
                Console.WriteLine("  Precisas de pelo menos 2 snapshots.");
                Console.WriteLine("  Usa a opção [1] para tirar um snapshot primeiro.\n");
                return;
            }

            Console.WriteLine($"  Antes : {older.Timestamp:dd/MM/yyyy HH:mm:ss}");
            Console.WriteLine($"  Agora : {newer.Timestamp:dd/MM/yyyy HH:mm:ss}");
            Console.WriteLine(new string('─', 60));

            var diff = SnapshotDiff.Compare(older, newer);

            if (diff.TotalModified + diff.TotalAdded + diff.TotalRemoved == 0)
            {
                Console.WriteLine("\n  Nenhuma alteração detectada.\n");
                return;
            }

            PrintDiff(diff);
            SaveReport(diff);
        }

        private void PrintDiff(DiffResult diff)
        {
            if (diff.TotalModified > 0)
            {
                Console.WriteLine($"\n  MODIFICADOS ({diff.TotalModified}):");
                foreach (var c in diff.Changes)
                {
                    if (c.Type != ChangeType.Modified) continue;
                    Console.WriteLine($"\n    [{c.BlockType}] {c.Device} / {c.BlockName}");

                    foreach (var net in c.Networks)
                    {
                        var icon = net.Type == ChangeType.Added   ? "+" :
                                   net.Type == ChangeType.Removed ? "-" : "~";
                        var title = string.IsNullOrEmpty(net.Title)
                            ? $"Network {net.Index}"
                            : $"Network {net.Index} — {net.Title}";

                        Console.WriteLine($"      {icon} {title}  [{net.Language}]");
                        foreach (var line in net.Changes)
                            Console.WriteLine($"        {line}");
                    }
                }
            }

            if (diff.TotalAdded > 0)
            {
                Console.WriteLine($"\n  ADICIONADOS ({diff.TotalAdded}):");
                foreach (var c in diff.Changes)
                {
                    if (c.Type != ChangeType.Added) continue;
                    Console.WriteLine($"    + [{c.BlockType}] {c.Device} / {c.BlockName}");
                }
            }

            if (diff.TotalRemoved > 0)
            {
                Console.WriteLine($"\n  REMOVIDOS ({diff.TotalRemoved}):");
                foreach (var c in diff.Changes)
                {
                    if (c.Type != ChangeType.Removed) continue;
                    Console.WriteLine($"    - [{c.BlockType}] {c.Device} / {c.BlockName}");
                }
            }

            Console.WriteLine();
        }

        private void SaveReport(DiffResult diff)
        {
            var reportDir = _snapshots.GetReportDir();
            Directory.CreateDirectory(reportDir);

            var file = Path.Combine(reportDir,
                $"diff_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var lines = new List<string>();
            lines.Add($"TIA Tracker — Relatório de Alterações");
            lines.Add($"Projecto : {_conn.Project.Name}");
            lines.Add($"Antes    : {diff.SnapshotOlder}");
            lines.Add($"Agora    : {diff.SnapshotNewer}");
            lines.Add($"Data     : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            lines.Add(new string('─', 60));

            foreach (var c in diff.Changes)
            {
                var icon = c.Type == ChangeType.Added ? "+" :
                           c.Type == ChangeType.Removed ? "-" : "~";
                lines.Add($"\n{icon} [{c.BlockType}] {c.Device}/{c.BlockName}");
                foreach (var n in c.Networks)
                {
                    var ni = n.Type == ChangeType.Added ? "+" :
                             n.Type == ChangeType.Removed ? "-" : "~";
                    lines.Add($"  {ni} Network {n.Index}" +
                              (string.IsNullOrEmpty(n.Title) ? "" : $" — {n.Title}") +
                              $" [{n.Language}]");
                    foreach (var l in n.Changes)
                        lines.Add($"    {l}");
                }
            }

            File.WriteAllLines(file, lines);
            Console.WriteLine($"  Relatório guardado: {file}\n");
        }

        // ── Compare Projecto vs PLC Online ────────────────────────────────────

        private void DoOnlineCompare()
        {
            Console.WriteLine("  Compare: Projecto (Offline) vs PLC (Online)\n");

            var comparer = new OnlineComparer(_conn.Project);
            var results  = comparer.CompareAll();

            if (results.Count == 0)
            {
                Console.WriteLine("  Nenhum PLC encontrado.\n");
                return;
            }

            int totalDiff = 0;

            foreach (var device in results)
            {
                Console.WriteLine($"\n  PLC: {device.Name}");
                Console.WriteLine($"  Offline: {device.LeftName}   |   Online: {device.RightName}");
                Console.WriteLine(new string('─', 60));

                int diffs = PrintCompareTree(device.Children, "  ", ref totalDiff, isLeaf: false);

                if (diffs == 0)
                    Console.WriteLine("  Projecto e PLC estao identicos.");
            }

            Console.WriteLine($"\n  Total de diferenças encontradas: {totalDiff}");

            if (totalDiff > 0)
            {
                SaveCompareReport(results, totalDiff);
            }

            Console.WriteLine();
        }

        private int PrintCompareTree(List<CompareItem> items, string indent, ref int totalDiff, bool isLeaf)
        {
            int count = 0;
            foreach (var item in items)
            {
                if (!OnlineComparer.IsDifferent(item.State)) continue;

                bool hasKids = item.Children.Count > 0;

                string tag;
                if (item.State == CompareResultState.LeftMissing)
                    tag = "[SÓ NO PLC]  ";
                else if (item.State == CompareResultState.RightMissing)
                    tag = "[SÓ NO PROJ] ";
                else if (item.State == CompareResultState.ObjectsDifferent)
                    tag = "[DIFERENTE]  ";
                else
                    tag = "[PASTA]      ";

                Console.WriteLine($"{indent}{tag} {item.Name}");

                // Conteúdo offline do bloco
                if (item.OfflineNetworks.Count > 0)
                {
                    Console.WriteLine($"{indent}             ┌─ PROJECTO (offline):");
                    foreach (var net in item.OfflineNetworks)
                        Console.WriteLine($"{indent}             │  {net}");
                    Console.WriteLine($"{indent}             └─────────────────────────────");
                }

                if (item.OfflineNetworks.Count > 0)
                {
                    Console.WriteLine($"{indent}             ⚠  PLC: tem versão diferente deste bloco");
                }

                // Conteúdo offline da tabela de tags
                if (item.OfflineTags.Count > 0)
                {
                    Console.WriteLine($"{indent}             ┌─ PROJECTO (tags offline):");
                    foreach (var tag2 in item.OfflineTags)
                        Console.WriteLine($"{indent}             │  {tag2}");
                    Console.WriteLine($"{indent}             └─ PLC: versão DIFERENTE da acima");
                }

                if (!hasKids || item.State == CompareResultState.ObjectsDifferent ||
                                item.State == CompareResultState.LeftMissing ||
                                item.State == CompareResultState.RightMissing)
                {
                    totalDiff++;
                    count++;
                }

                if (hasKids)
                    PrintCompareTree(item.Children, indent + "    ", ref totalDiff, isLeaf: !hasKids);
            }
            return count;
        }

        private void SaveCompareReport(List<CompareItem> results, int totalDiff)
        {
            var dir  = _snapshots.GetReportDir();
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"compare_online_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var lines = new List<string>();
            lines.Add("TIA Tracker — Compare Projecto vs PLC Online");
            lines.Add($"Data     : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            lines.Add($"Projecto : {_conn.Project.Name}");
            lines.Add(new string('-', 50));

            foreach (var device in results)
            {
                lines.Add($"\nPLC: {device.Name}");
                WriteCompareLines(device.Children, "  ", lines);
            }

            lines.Add($"\nTotal diferenças: {totalDiff}");
            File.WriteAllLines(file, lines);
            Console.WriteLine($"  Relatório: {file}");
        }

        private void WriteCompareLines(List<CompareItem> items, string indent, List<string> lines)
        {
            foreach (var item in items)
            {
                if (!OnlineComparer.IsDifferent(item.State)) continue;
                lines.Add($"{indent}[{OnlineComparer.StateLabel(item.State)}] {item.Name}");
                if (item.OfflineNetworks.Count > 0)
                {
                    lines.Add($"{indent}  PROJECTO (offline):");
                    foreach (var net in item.OfflineNetworks)
                        lines.Add($"{indent}    {net}");
                    lines.Add($"{indent}  PLC: versão diferente");
                }
                if (item.OfflineTags.Count > 0)
                {
                    lines.Add($"{indent}  PROJECTO (tags):");
                    foreach (var t in item.OfflineTags)
                        lines.Add($"{indent}    {t}");
                    lines.Add($"{indent}  PLC: versão diferente");
                }
                if (item.Children.Count > 0)
                    WriteCompareLines(item.Children, indent + "    ", lines);
            }
        }

        // ── Online Check (legado) ─────────────────────────────────────────────

        private void DoOnlineCheck()
        {
            var checker = new OnlineChecker(_conn.Project);

            // Estado do projecto (sem ligar ao PLC)
            var statuses = checker.CheckAll();

            if (statuses.Count == 0)
            {
                Console.WriteLine("  Nenhum PLC encontrado no projecto.\n");
                return;
            }

            Console.WriteLine("  Estado do projecto:\n");
            foreach (var s in statuses)
            {
                Console.WriteLine($"  PLC                 : {s.DeviceName}");
                Console.WriteLine($"  Projecto modificado : {(s.IsModified ? "SIM — tem alterações por guardar" : "Não")}");
                if (s.IsModified)
                {
                    Console.WriteLine($"  Modificado por      : {s.ModifiedBy}");
                    Console.WriteLine($"  Em                  : {s.LastModified:dd/MM/yyyy HH:mm:ss}");
                }
                Console.WriteLine();
            }

            // Só vai online se o utilizador pedir
            Console.Write("  Queres ligar ao PLC fisicamente para verificar estado? (s/n): ");
            if (Console.ReadLine()?.Trim().ToLower() == "s")
            {
                Console.WriteLine("  A ligar ao PLC...\n");
                var online = checker.GoOnlineAll();
                foreach (var s in online)
                {
                    Console.WriteLine($"  {s.DeviceName}  →  {s.State}");
                }
                Console.WriteLine();
            }
        }

        // ── Ler blocos do projecto ────────────────────────────────────────────

        private void DoReadBlocks()
        {
            Console.WriteLine("  A ler todos os blocos do projecto...\n");

            var reader = new ProjectReader(_conn.Project);
            var blocks = reader.ReadAllBlocks();

            if (blocks.Count == 0)
            {
                Console.WriteLine("  Nenhum bloco encontrado.\n");
                return;
            }

            Console.WriteLine();
            Console.WriteLine(new string('═', 60));

            foreach (var block in blocks)
            {
                PrintBlock(block);
            }

            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"\n  Total: {blocks.Count} blocos lidos.\n");

            var callGraph  = ProjectReader.BuildCallGraph(blocks);
            var graphLines = BuildCallGraphLines(blocks, callGraph);
            PrintCallGraph(graphLines);

            SaveBlocksReport(blocks, graphLines);
        }

        private void DoCallGraph()
        {
            Console.WriteLine("  A construir grafo de chamadas...\n");

            var reader    = new ProjectReader(_conn.Project);
            var blocks    = reader.ReadAllBlocks();
            var callGraph = ProjectReader.BuildCallGraph(blocks);
            var lines     = BuildCallGraphLines(blocks, callGraph);

            PrintCallGraph(lines);

            // Salvar ficheiro dedicado
            var dir  = _snapshots.GetReportDir();
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"call_graph_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var report = new List<string>();
            report.Add("TIA Tracker — Grafo de Chamadas");
            report.Add($"Projecto : {_conn.Project.Name}");
            report.Add($"Data     : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            report.Add(new string('═', 60));
            report.Add("");
            report.AddRange(lines);
            File.WriteAllLines(file, report);
            Console.WriteLine($"  Grafo guardado: {file}\n");
        }

        private static void PrintBlock(BlockInfo block)
        {
            Console.WriteLine($"\n  [{block.Type}] {block.Device} / {block.Name}  (#{block.Number}, {block.Language})");
            Console.WriteLine(new string('─', 50));

            // Interface sections (Input / Output / InOut / Static / Temp)
            foreach (var sec in block.Interface)
            {
                if (sec.Members.Count == 0) continue;
                Console.WriteLine($"  -- {sec.Name} --");
                PrintMembers(sec.Members, "    ");
            }

            if (block.Interface.Any(s => s.Members.Count > 0))
                Console.WriteLine();

            // Networks / logic
            if (block.Networks.Count == 0)
            {
                Console.WriteLine("    (sem networks)");
                return;
            }

            foreach (var net in block.Networks)
            {
                var header = string.IsNullOrEmpty(net.Title)
                    ? $"  Network {net.Index} [{net.Language}]"
                    : $"  Network {net.Index} — {net.Title} [{net.Language}]";
                Console.WriteLine(header);

                if (net.Lines.Count == 0)
                    Console.WriteLine("    (vazio)");
                else
                    foreach (var line in net.Lines)
                        Console.WriteLine($"    {line}");

                Console.WriteLine();
            }
        }

        private static void PrintMembers(List<MemberInfo> members, string indent)
        {
            foreach (var m in members)
            {
                var init    = string.IsNullOrEmpty(m.InitialValue) ? "" : $" := {m.InitialValue}";
                var comment = string.IsNullOrEmpty(m.Comment)      ? "" : $"  // {m.Comment}";
                Console.WriteLine($"{indent}{m.Name} : {m.DataType}{init}{comment}");
                if (m.Members.Count > 0)
                    PrintMembers(m.Members, indent + "  ");
            }
        }

        // ── Call Graph ────────────────────────────────────────────────────────

        private static List<string> BuildCallGraphLines(
            List<BlockInfo> blocks,
            Dictionary<string, List<ProjectReader.CallEdge>> graph)
        {
            var lines    = new List<string>();
            var blockMap = blocks
                .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var called = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in graph)
                foreach (var e in kv.Value)
                    called.Add(e.Name);

            var roots = blocks
                .Where(b => b.Type != "DB" && b.Type != "iDB" && (b.Type == "OB" || !called.Contains(b.Name)))
                .OrderBy(b => b.Type == "OB" ? 0 : 1)
                .ThenBy(b => b.Number);

            foreach (var root in roots)
            {
                var rootMeta = $"  [{root.Type}{root.Number}]  {root.Name}  · {root.Language} · {root.Networks.Count} redes";
                lines.Add(rootMeta);
                AppendCallNode(root.Name, graph, blockMap, 1,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase), lines);
                lines.Add("");
            }

            lines.Add("── Referência inversa ──");
            lines.Add($"  {"Bloco",-35} {"Tipo",-8} {"Lang",-5} {"Redes",-6}  Chamado por");
            lines.Add("  " + new string('─', 70));
            foreach (var block in blocks.Where(b => b.Type != "OB").OrderBy(b => b.Type).ThenBy(b => b.Name))
            {
                var callers = graph
                    .Where(kv => kv.Value.Any(c => string.Equals(c.Name, block.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(kv =>
                    {
                        var inst = kv.Value.FirstOrDefault(c => string.Equals(c.Name, block.Name, StringComparison.OrdinalIgnoreCase))?.Instance;
                        return inst != null ? $"{kv.Key} (via {inst})" : kv.Key;
                    })
                    .OrderBy(s => s).ToList();
                if (callers.Count == 0) continue;
                var num = $"{block.Type}{block.Number}";
                lines.Add($"  {block.Name,-35} {num,-8} {block.Language,-5} {block.Networks.Count,-6}  ← {string.Join(", ", callers)}");
            }

            return lines;
        }

        private static void AppendCallNode(
            string name,
            Dictionary<string, List<ProjectReader.CallEdge>> graph,
            Dictionary<string, BlockInfo> blockMap,
            int depth,
            HashSet<string> stack,
            List<string> lines)
        {
            if (stack.Contains(name))
            {
                lines.Add($"{new string(' ', depth * 2)}↻ (recursão detectada)");
                return;
            }

            if (!graph.TryGetValue(name, out var calls) || calls.Count == 0)
            {
                if (depth == 1) lines.Add("  (sem chamadas)");
                return;
            }

            stack.Add(name);
            foreach (var edge in calls)
            {
                blockMap.TryGetValue(edge.Name, out var cb);
                var num   = cb != null ? $"{edge.Type}{cb.Number}" : edge.Type;
                var lang  = cb != null ? $" · {cb.Language}" : "";
                var nets  = cb != null ? $" · {cb.Networks.Count} redes" : "";
                var inst  = edge.Instance != null ? $"  [iDB: {edge.Instance}]" : "";
                lines.Add($"{new string(' ', depth * 2)}→ [{num,-6}]  {edge.Name}{lang}{nets}{inst}");
                AppendCallNode(edge.Name, graph, blockMap, depth + 1, stack, lines);
            }
            stack.Remove(name);
        }

        private static void PrintCallGraph(List<string> graphLines)
        {
            Console.WriteLine("  GRAFO DE CHAMADAS");
            Console.WriteLine(new string('═', 60));
            foreach (var line in graphLines)
                Console.WriteLine(string.IsNullOrEmpty(line) ? "" : $"  {line}");
            Console.WriteLine();
        }

        private void SaveBlocksReport(List<BlockInfo> blocks, List<string> callGraphLines)
        {
            var dir  = _snapshots.GetReportDir();
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"blocos_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var lines = new List<string>();
            lines.Add("TIA Tracker — Leitura de Blocos do Projecto");
            lines.Add($"Projecto : {_conn.Project.Name}");
            lines.Add($"Data     : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            lines.Add(new string('═', 60));

            foreach (var block in blocks)
            {
                lines.Add($"\n[{block.Type}] {block.Device} / {block.Name}  (#{block.Number}, {block.Language})");
                lines.Add(new string('─', 50));

                foreach (var sec in block.Interface)
                {
                    if (sec.Members.Count == 0) continue;
                    lines.Add($"  -- {sec.Name} --");
                    AppendMembers(sec.Members, "    ", lines);
                }

                foreach (var net in block.Networks)
                {
                    var header = string.IsNullOrEmpty(net.Title)
                        ? $"  Network {net.Index} [{net.Language}]"
                        : $"  Network {net.Index} — {net.Title} [{net.Language}]";
                    lines.Add(header);
                    foreach (var ln in net.Lines)
                        lines.Add($"    {ln}");
                }
            }

            lines.Add($"\nTotal: {blocks.Count} blocos");

            lines.Add("");
            lines.Add(new string('═', 60));
            lines.Add("GRAFO DE CHAMADAS");
            lines.Add(new string('═', 60));
            lines.AddRange(callGraphLines);

            File.WriteAllLines(file, lines);
            Console.WriteLine($"  Relatório guardado: {file}\n");
        }

        private static void AppendMembers(List<MemberInfo> members, string indent, List<string> lines)
        {
            foreach (var m in members)
            {
                var init    = string.IsNullOrEmpty(m.InitialValue) ? "" : $" := {m.InitialValue}";
                var comment = string.IsNullOrEmpty(m.Comment)      ? "" : $"  // {m.Comment}";
                lines.Add($"{indent}{m.Name} : {m.DataType}{init}{comment}");
                if (m.Members.Count > 0)
                    AppendMembers(m.Members, indent + "  ", lines);
            }
        }

        // ── Lista snapshots ───────────────────────────────────────────────────

        private void DoListSnapshots()
        {
            var snaps = _snapshots.GetSnapshots(_conn.Project.Name);

            if (snaps.Count == 0)
            {
                Console.WriteLine("  Nenhum snapshot encontrado.\n");
                return;
            }

            Console.WriteLine($"  Snapshots do projecto '{_conn.Project.Name}':\n");
            for (int i = 0; i < snaps.Count; i++)
            {
                var s = snaps[i];
                Console.WriteLine($"  [{i + 1}] {s.Timestamp:dd/MM/yyyy HH:mm:ss}" +
                                  $"  —  {s.TotalBlocks} blocos, {s.TotalTagTables} tag tables");
            }
            Console.WriteLine();
        }
    }
}
