using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Core.Commands;
using UnityEngine;

namespace Core.Recording
{
    /// <summary>
    /// What a recording captures and where it goes. Everything is opt-in: the game decides whether to
    /// record at all, when to start, and which sources to add.
    /// </summary>
    public sealed class RecordingOptions
    {
        /// <summary>Folder that holds the recordings (one subfolder each).</summary>
        public string Root;
        /// <summary>This recording's folder name; it should sort by time (start it with a timestamp).</summary>
        public string Name;
        /// <summary>Recordings to keep under Root, this one included; older ones are deleted. 0 keeps all.</summary>
        public int Keep = 12;
        /// <summary>Every successfully executed command, with its public fields and properties (commands.tsv).</summary>
        public bool Commands = true;
        /// <summary>Warnings, errors and exceptions from the Unity log (log.tsv).</summary>
        public bool Log = true;
        /// <summary>Decisions and why, from SessionRecorder.Note (notes.tsv).</summary>
        public bool Notes = true;
        /// <summary>Extra entries for meta.json (player, scenario, build, ...).</summary>
        public readonly Dictionary<string, string> Meta = new Dictionary<string, string>();
    }

    /// <summary>
    /// A part of the game state written to a recording. Begin declares its tables (and may write the
    /// starting state); Record is called on every SessionRecorder.Record, which the game calls when its
    /// tick is complete. Sources only read state.
    /// </summary>
    public interface IRecordingSource
    {
        void Begin(SessionRecorder recorder);
        void Record(SessionRecorder recorder);
    }

    /// <summary>One tab-separated file of a recording. Time tables start every row with tick and date.</summary>
    public sealed class RecordingTable
    {
        readonly SessionRecorder owner;
        readonly StreamWriter writer;
        readonly bool timed;

        internal RecordingTable(SessionRecorder owner, StreamWriter writer, bool timed)
        {
            this.owner = owner;
            this.writer = writer;
            this.timed = timed;
        }

        /// <summary>Write a row: the fields already joined with tabs (see SessionRecorder.Clean for free text).</summary>
        public void Row(string fields)
        {
            if (owner.Stopped) return;
            if (timed)
            {
                writer.Write(owner.Tick);
                writer.Write('\t');
                writer.Write(owner.Date);
                writer.Write('\t');
            }
            writer.WriteLine(fields);
        }

        internal void Flush() => writer.Flush();
        internal void Close() => writer.Dispose();
    }

    /// <summary>
    /// ENGINE: records a play session as tab-separated files that a person or a tool can read afterwards.
    /// A mechanism only: nothing records unless the game calls Start, and the game chooses the tables.
    ///
    /// Layout: Root/Name/ with meta.json, countries.tsv (id, tag), and one .tsv per table. Time tables
    /// start with tick (TimeManager.CurrentTick) and date (YYYY-MM-DD HH). Built in, each optional:
    /// commands.tsv (via CommandProcessor.CommandExecuted, inside the tick that ran the command),
    /// log.tsv (warnings and errors) and notes.tsv (Note: decisions and why). Add sources for the rest,
    /// e.g. ProvinceControlSource, or the game's own.
    ///
    /// Recording only reads state, so it never affects determinism. Writes are buffered and flushed
    /// once per game day and on Stop.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        /// <summary>The recording in progress, null when none.</summary>
        public static SessionRecorder Current { get; private set; }

        /// <summary>True while a recording is open with notes on: callers can skip building note text.</summary>
        public static bool IsRecording => Current != null && Current.notes != null;

        public GameState GameState { get; }
        public string Directory { get; }
        public bool Stopped { get; private set; }

        readonly RecordingOptions options;
        readonly Dictionary<string, RecordingTable> tables = new Dictionary<string, RecordingTable>();
        readonly List<IRecordingSource> sources = new List<IRecordingSource>();
        readonly Dictionary<ushort, string> tags = new Dictionary<ushort, string>();
        readonly RecordingTable commands, log, notes;
        ulong lastFlushDay = ulong.MaxValue;
        bool inLog;

        /// <summary>Start recording (stops the previous one). Returns null when the folder can't be created.</summary>
        public static SessionRecorder Start(GameState gameState, RecordingOptions options)
        {
            Current?.Stop();
            try
            {
                Current = new SessionRecorder(gameState, options);
            }
            catch (Exception e)
            {
                ArchonLogger.LogWarning($"SessionRecorder: cannot start a recording in {options?.Root}: {e.Message}", "core_recording");
                Current = null;
            }
            return Current;
        }

        SessionRecorder(GameState gameState, RecordingOptions options)
        {
            if (gameState == null) throw new ArgumentNullException(nameof(gameState));
            if (string.IsNullOrEmpty(options?.Root) || string.IsNullOrEmpty(options.Name))
                throw new ArgumentException("RecordingOptions needs Root and Name");
            GameState = gameState;
            this.options = options;
            System.IO.Directory.CreateDirectory(options.Root);
            Prune(options.Root, options.Keep);
            Directory = Path.Combine(options.Root, options.Name);
            System.IO.Directory.CreateDirectory(Directory);

            WriteMeta();
            var countries = Table("countries", "id\ttag", timed: false);
            var ids = gameState.Countries.GetAllCountryIds(Unity.Collections.Allocator.Temp);
            var sorted = new List<ushort>(ids);
            ids.Dispose();
            sorted.Sort();
            foreach (ushort c in sorted) if (c != 0) countries.Row($"{c}\t{Tag(c)}");

            if (options.Commands)
            {
                commands = Table("commands", "command\tfields");
                gameState.CommandProcessor.CommandExecuted += OnCommand;
            }
            if (options.Log)
            {
                log = Table("log", "level\tmessage\twhere");
                Application.logMessageReceived += OnLog;
            }
            if (options.Notes)
                notes = Table("notes", "system\tkind\tcountry\tunit\tprovince\ttarget\ttext");
            ArchonLogger.Log($"SessionRecorder: recording to {Directory}", "core_recording");
        }

        public ulong Tick => GameState.Time.CurrentTick;

        public string Date
        {
            get
            {
                var t = GameState.Time.GetCurrentGameTime();
                return $"{t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}";
            }
        }

        /// <summary>A table (created on first use). Time tables start with tick and date; columns lists the rest.</summary>
        public RecordingTable Table(string name, string columns, bool timed = true)
        {
            if (tables.TryGetValue(name, out var table)) return table;
            var writer = new StreamWriter(Path.Combine(Directory, name + ".tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            writer.WriteLine(timed ? "tick\tdate\t" + columns : columns);
            table = new RecordingTable(this, writer, timed);
            tables[name] = table;
            return table;
        }

        /// <summary>Add a source: Begin now, Record on every Record call.</summary>
        public void Add(IRecordingSource source)
        {
            if (Stopped || source == null) return;
            sources.Add(source);
            try { source.Begin(this); }
            catch (Exception e) { ArchonLogger.LogError($"SessionRecorder: {source.GetType().Name}.Begin failed - {e.Message}", "core_recording"); }
        }

        /// <summary>Let every source write this tick. Call when the game's tick is complete.</summary>
        public void Record()
        {
            if (Stopped) return;
            foreach (var s in sources)
            {
                try { s.Record(this); }
                catch (Exception e) { ArchonLogger.LogError($"SessionRecorder: {s.GetType().Name}.Record failed - {e.Message}", "core_recording"); }
            }
            ulong day = Tick / 24;
            if (day != lastFlushDay) { lastFlushDay = day; Flush(); }
        }

        /// <summary>A decision and why (notes.tsv). Zeros where a column doesn't apply. No-op when not recording.</summary>
        public static void Note(string system, string kind, ushort country, ushort unit, ushort province, ushort target, string text)
        {
            var r = Current;
            if (r == null || r.notes == null) return;
            r.notes.Row($"{Clean(system)}\t{Clean(kind)}\t{r.Tag(country)}\t{unit}\t{province}\t{target}\t{Clean(text)}");
        }

        public void Flush()
        {
            foreach (var t in tables.Values)
            {
                try { t.Flush(); } catch { /* best effort */ }
            }
        }

        public void Stop()
        {
            if (Stopped) return;
            Stopped = true;
            if (commands != null && GameState.CommandProcessor != null) GameState.CommandProcessor.CommandExecuted -= OnCommand;
            if (log != null) Application.logMessageReceived -= OnLog;
            foreach (var t in tables.Values)
            {
                try { t.Close(); } catch { /* closing anyway */ }
            }
            if (Current == this) Current = null;
        }

        public void Dispose() => Stop();

        /// <summary>The country's tag, or its id when it has none; "-" for 0.</summary>
        public string Tag(ushort country)
        {
            if (country == 0) return "-";
            if (!tags.TryGetValue(country, out string tag))
            {
                tag = GameState.Countries.GetCountryTag(country);
                tags[country] = tag = string.IsNullOrEmpty(tag) ? country.ToString() : tag;
            }
            return tag;
        }

        /// <summary>Free text made safe for a field: no tabs or line breaks.</summary>
        public static string Clean(string s) => s == null ? "" : s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        /// <summary>Ids (or anything) joined with commas, empty for none.</summary>
        public static string Join<T>(IEnumerable<T> items) => items == null ? "" : string.Join(",", items);

        // --- built in ------------------------------------------------------------------------------

        void WriteMeta()
        {
            var sb = new StringBuilder("{\n");
            void Entry(string k, string v) => sb.Append($"  \"{Escape(k)}\": \"{Escape(v)}\",\n");
            foreach (var kv in options.Meta) Entry(kv.Key, kv.Value);
            Entry("start", Date);
            Entry("recorded", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append($"  \"start_tick\": {Tick}\n}}\n");
            File.WriteAllText(Path.Combine(Directory, "meta.json"), sb.ToString());
        }

        static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        static void Prune(string root, int keep)
        {
            if (keep <= 0) return;
            var dirs = new List<string>(System.IO.Directory.GetDirectories(root));
            dirs.Sort(StringComparer.Ordinal);
            for (int i = 0; i < dirs.Count - (keep - 1); i++)
            {
                try { System.IO.Directory.Delete(dirs[i], true); } catch { /* in use: next time */ }
            }
        }

        void OnCommand(ICommand command)
        {
            if (Stopped) return;
            commands.Row($"{command.GetType().Name}\t{Clean(Fields(command))}");
        }

        static string Fields(object o)
        {
            var sb = new StringBuilder();
            var type = o.GetType();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(f.Name).Append('=').Append(Value(f.GetValue(o)));
                if (sb.Length > 4000) return sb.ToString();
            }
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0 || p.Name == "EstimatedNetworkSize") continue;
                object v;
                try { v = p.GetValue(o); } catch { continue; }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(p.Name).Append('=').Append(Value(v));
                if (sb.Length > 4000) break;
            }
            return sb.ToString();
        }

        static string Value(object v)
        {
            if (v == null) return "null";
            if (v is string s) return s;
            if (v is IEnumerable e)
            {
                var parts = new List<string>();
                foreach (var x in e)
                {
                    parts.Add(x?.ToString() ?? "null");
                    if (parts.Count >= 200) { parts.Add("..."); break; }
                }
                return string.Join(",", parts);
            }
            return v.ToString();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (Stopped || inLog || type == LogType.Log) return;
            inLog = true; // a failure while writing must not recurse
            try
            {
                string where = "";
                if (!string.IsNullOrEmpty(stack))
                {
                    int nl = stack.IndexOf('\n');
                    where = nl > 0 ? stack.Substring(0, nl) : stack;
                }
                log.Row($"{type}\t{Clean(message)}\t{Clean(where)}");
            }
            finally { inLog = false; }
        }
    }

    /// <summary>
    /// Built-in source: every province whose controller or owner changed since the last Record
    /// (control.tsv: province, from, to = controller, falling back to the owner when there is none;
    /// owner = the owner now). Optionally limited to some provinces (e.g. land only).
    /// </summary>
    public sealed class ProvinceControlSource : IRecordingSource
    {
        readonly Func<ushort, bool> include;
        ushort[] provinces;
        ushort[] controller, owner;
        RecordingTable table;

        public ProvinceControlSource(Func<ushort, bool> include = null) => this.include = include;

        public void Begin(SessionRecorder r)
        {
            table = r.Table("control", "province\tfrom\tto\towner");
            var ids = r.GameState.Provinces.GetAllProvinceIds(Unity.Collections.Allocator.Temp);
            var list = new List<ushort>(ids.Length);
            int max = 0;
            foreach (ushort id in ids)
            {
                max = Math.Max(max, id);
                if (id != 0 && (include == null || include(id))) list.Add(id);
            }
            ids.Dispose();
            list.Sort();
            provinces = list.ToArray();
            controller = new ushort[max + 1];
            owner = new ushort[max + 1];
            var p = r.GameState.Provinces;
            foreach (ushort id in provinces)
            {
                owner[id] = p.GetProvinceOwner(id);
                controller[id] = Controller(p, id, owner[id]);
            }
        }

        static ushort Controller(Systems.ProvinceSystem p, ushort id, ushort ownerId)
        {
            ushort c = p.GetProvinceController(id);
            return c != 0 ? c : ownerId;
        }

        public void Record(SessionRecorder r)
        {
            var p = r.GameState.Provinces;
            foreach (ushort id in provinces)
            {
                ushort o = p.GetProvinceOwner(id);
                ushort c = Controller(p, id, o);
                if (c == controller[id] && o == owner[id]) continue;
                table.Row($"{id}\t{r.Tag(controller[id])}\t{r.Tag(c)}\t{r.Tag(o)}");
                controller[id] = c;
                owner[id] = o;
            }
        }
    }
}
