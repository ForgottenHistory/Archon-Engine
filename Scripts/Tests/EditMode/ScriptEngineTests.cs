using System.Collections.Generic;
using NUnit.Framework;
using Scripting;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for ScriptEngine - the rules the GAME's scripts rely on.
    ///
    /// WHY THIS MATTERS: scripts are content, written by many hands and by tools. A script that is wrong must not
    /// take the game down or leave the engine in a state where the next script fails too: errors come back as
    /// false with a file and a line, a missing method is reported and returns false, an endless loop is stopped,
    /// and the engine keeps working afterwards.
    /// </summary>
    public class ScriptEngineTests
    {
        sealed class Country : ScriptObject
        {
            public readonly string Tag;
            public double Gold;
            public Country(ScriptType type, string tag) : base(type) { Tag = tag; }
        }

        ScriptEngine engine;
        ScriptType countryType;
        Country red, blue;
        readonly List<Country> all = new List<Country>();

        [SetUp]
        public void SetUp()
        {
            engine = new ScriptEngine();
            countryType = engine.DefineType("country");
            countryType.SetMethod("add_gold", (ref ScriptCall call) => call.Self<Country>().Gold += call.Number(1));
            countryType.SetMethod("gold", (ref ScriptCall call) => call.Return(call.Self<Country>().Gold));
            countryType.SetMethod("is_tag", (ref ScriptCall call) => call.Return(call.Self<Country>().Tag == call.String(1)));
            countryType.SetMethod("rich", (ref ScriptCall call) => call.Return((call.Self<Country>().Gold >= 100) == call.Bool(1)));
            // a method that calls back into the script: any other country the function accepts
            countryType.SetMethod("any_other", (ref ScriptCall call) =>
            {
                var self = call.Self<Country>();
                var filter = call.Function(1);
                bool any = false;
                foreach (var other in all)
                    if (other != self && call.Engine.CallBool(filter, other)) { any = true; break; }
                call.Return(any);
            });
            countryType.SetMethod("others", (ref ScriptCall call) =>
            {
                var self = call.Self<Country>();
                call.ReturnList(all.FindAll(c => c != self));
            });
            all.Clear();
            all.Add(red = new Country(countryType, "RED"));
            all.Add(blue = new Country(countryType, "BLU"));
            engine.SetFunction("country", (ref ScriptCall call) =>
            {
                string tag = call.String(0);
                call.Return(all.Find(c => c.Tag == tag));
            });
        }

        [TearDown]
        public void TearDown() => engine.Dispose();

        ScriptFunctionRef Function(string body)
        {
            Assert.IsTrue(engine.Run("f = " + body, "test.lua"), engine.LastError);
            var function = engine.GetGlobal("f").Function;
            Assert.IsTrue(function.IsValid);
            return function;
        }

        [Test]
        public void Method_ChangesTheObject()
        {
            Assert.IsTrue(engine.Call(Function("function(c) c:add_gold(25) c:add_gold(5) end"), red, out _), engine.LastError);
            Assert.AreEqual(30, red.Gold);
            Assert.AreEqual(0, blue.Gold);
        }

        [Test]
        public void Call_ReturnsTheScriptsValue()
        {
            red.Gold = 7;
            Assert.AreEqual(14, engine.CallNumber(Function("function(c) return c:gold() * 2 end"), red));
            Assert.IsTrue(engine.CallBool(Function("function(c) return c:is_tag('RED') end"), red));
            Assert.IsFalse(engine.CallBool(Function("function(c) return c:is_tag('RED') end"), blue));
        }

        [Test]
        public void MissingArgument_ReadsAsTheFallback()
        {
            red.Gold = 500;
            Assert.IsTrue(engine.CallBool(Function("function(c) return c:rich() end"), red));
            Assert.IsFalse(engine.CallBool(Function("function(c) return c:rich(false) end"), red));
        }

        [Test]
        public void Method_CanCallBackIntoTheScript()
        {
            blue.Gold = 150;
            Assert.IsTrue(engine.CallBool(Function("function(c) return c:any_other(function(o) return o:gold() > 100 end) end"), red));
            Assert.IsFalse(engine.CallBool(Function("function(c) return c:any_other(function(o) return o:gold() > 100 end) end"), blue));
        }

        [Test]
        public void ReturnedList_LoopsInOrder()
        {
            var give = Function("function(c) for _, o in ipairs(c:others()) do o:add_gold(1) end end");
            Assert.IsTrue(engine.Call(give, red, out _), engine.LastError);
            Assert.AreEqual(0, red.Gold);
            Assert.AreEqual(1, blue.Gold);
        }

        [Test]
        public void SameObject_IsEqualInLua()
        {
            Assert.IsTrue(engine.CallBool(Function("function(c) return country('RED') == c end"), red));
            Assert.IsFalse(engine.CallBool(Function("function(c) return country('BLU') == c end"), red));
            Assert.IsTrue(engine.CallBool(Function("function(c) return country('nowhere') == nil end"), red));
        }

        [Test]
        public void Table_ReadsFieldsListsAndFunctions()
        {
            ScriptTable kept = default;
            engine.SetFunction("define", (ref ScriptCall call) => kept = call.Table(0));
            Assert.IsTrue(engine.Run("define { id = 'harvest', cost = 10, tags = { 'a', 'b' }, hidden = false, "
                                     + "reward = function(c) c:add_gold(3) end }", "defs.lua"), engine.LastError);
            Assert.IsTrue(kept.IsValid);
            Assert.AreEqual("harvest", kept.String("id"));
            Assert.AreEqual(10, kept.Number("cost"));
            Assert.AreEqual(2, kept.Table("tags").Length);
            Assert.AreEqual("b", kept.Table("tags")[2].String);
            Assert.IsFalse(kept.Bool("hidden", true));
            Assert.IsTrue(kept.Bool("absent", true));
            Assert.IsFalse(kept.Has("absent"));
            var names = new List<string>();
            kept.FieldNames(names);
            CollectionAssert.AreEqual(new[] { "cost", "hidden", "id", "reward", "tags" }, names);
            Assert.IsTrue(engine.Call(kept.Function("reward"), red, out _), engine.LastError);
            Assert.AreEqual(3, red.Gold);
        }

        [Test]
        public void MissingMethod_IsReportedOnceAndReturnsFalse()
        {
            var reported = new List<string>();
            engine.MissingMember += (type, member) => reported.Add(type + "." + member);
            var f = Function("function(c) c:add_gold(1) return c:has_dragons() end");
            Assert.IsFalse(engine.CallBool(f, red, onError: true));   // the script ran: false is its answer
            Assert.IsFalse(engine.CallBool(f, red, onError: true));
            Assert.AreEqual(2, red.Gold);
            CollectionAssert.AreEqual(new[] { "country.has_dragons" }, reported);
            Assert.IsFalse(countryType.HasMethod("has_dragons"));
        }

        [Test]
        public void MissingGlobal_IsAnErrorUnlessStubbed()
        {
            Assert.IsFalse(engine.Run("made_up_function(1)", "plain.lua"));
            StringAssert.Contains("plain.lua:1", engine.LastError);

            using (var lenient = new ScriptEngine(stubMissingGlobals: true))
            {
                Assert.IsTrue(lenient.Run("result = made_up_function(1)", "lenient.lua"), lenient.LastError);
                Assert.AreEqual(ScriptValueKind.Boolean, lenient.GetGlobal("result").Kind);
                Assert.IsFalse(lenient.GetGlobal("result").Truthy);
                CollectionAssert.Contains(lenient.Missing, ((string)null, "made_up_function"));
            }
        }

        [Test]
        public void Errors_NameFileAndLine_AndTheEngineKeepsWorking()
        {
            string heard = null;
            engine.Error += message => heard = message;

            Assert.IsFalse(engine.Run("local x = = 3", "syntax.lua"));
            StringAssert.Contains("syntax.lua:1", engine.LastError);
            Assert.AreEqual(engine.LastError, heard);

            Assert.IsFalse(engine.Run("local t = nil\nreturn t.x", "runtime.lua"));
            StringAssert.Contains("runtime.lua:2", engine.LastError);

            engine.SetFunction("refuse", (ref ScriptCall call) => call.Fail("not allowed here"));
            Assert.IsFalse(engine.Run("\n\nrefuse()", "csharp.lua"));
            StringAssert.Contains("not allowed here", engine.LastError);
            StringAssert.Contains("csharp.lua:3", engine.LastError);

            // an error inside a callback fails the whole outer call, once
            Assert.IsFalse(engine.Call(Function("function(c) return c:any_other(function(o) return o.x.y end) end"), red, out _));

            Assert.IsTrue(engine.Call(Function("function(c) c:add_gold(1) end"), red, out _), engine.LastError);
            Assert.IsNull(engine.LastError);
            Assert.AreEqual(1, red.Gold);
        }

        [Test]
        public void EndlessLoop_IsStopped()
        {
            engine.InstructionLimit = 1_000_000;
            Assert.IsFalse(engine.Run("while true do end", "loop.lua"));
            StringAssert.Contains("instructions", engine.LastError);
            // the budget is per top-level call, not used up for good
            Assert.AreEqual(5050, engine.CallNumber(Function("function() local s = 0 for i = 1, 100 do s = s + i end return s end"), ScriptValue.Nil));
        }

        [Test]
        public void Sandbox_HasNoFilesNoLoadingNoRandom()
        {
            foreach (string name in new[] { "io", "os", "debug", "package", "require", "dofile", "loadfile", "load", "loadstring", "collectgarbage" })
                Assert.IsTrue(engine.GetGlobal(name).IsNil, name);
            Assert.IsTrue(engine.Run("r = math.random == nil and math.randomseed == nil and math.floor(2.5) == 2", "sandbox.lua"), engine.LastError);
            Assert.IsTrue(engine.GetGlobal("r").Truthy);
            Assert.IsTrue(engine.Run("s = string.format('%d-%s', 3, table.concat({ 'a', 'b' }, '+'))", "libs.lua"), engine.LastError);
            Assert.AreEqual("3-a+b", engine.GetGlobal("s").String);
        }

        [Test]
        public void TypeMetatable_IsOutOfTheScriptsReach()
        {
            Assert.IsTrue(engine.Call(Function("function(c) return getmetatable(c) end"), red, out var meta), engine.LastError);
            Assert.AreNotEqual(ScriptValueKind.Table, meta.Kind);
        }
    }
}
