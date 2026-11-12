using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace DBCompare {
    internal static partial class Tests {
        static void PostgresTextTests() {
            var pg=DatabaseEngine.PostgreSql;
            Expect(SqlDiff.Classify("SELECT a[ 1 ]","SELECT a[1]",pg)==Result.Format,"PG array brackets");
            Expect(SqlDiff.Classify("SELECT 1_000","SELECT 1 _000",pg)==Result.Changed,"PG numeric separator versus column alias");
            Expect(SqlDiff.Classify("SELECT 1+-2","SELECT 1+ -2",pg)==Result.Format,"PG binary operator and unary sign");
            Expect(SqlDiff.Classify("SELECT $tag$a  b$tag$","SELECT $tag$a b$tag$",pg)==Result.Changed,"PG dollar literal whitespace");
            Expect(SqlDiff.Classify("SELECT E'a\\\'  b'","SELECT E'a\\\' b'",pg)==Result.Changed,"PG escape literal");
            string a="AS $body$BEGIN RETURN $x$a b$x$; END$body$",b="AS $body$BEGIN\n RETURN $x$a b$x$; END$body$";
            Expect(SqlDiff.Classify(a,b,pg,true)==Result.Format && SqlDiff.Detail(a,b,pg,true).Where(p=>p.B!=null).All(p=>p.B.Level<2),"PG routine indentation is blue");
            Expect(SqlDiff.Classify(a,b.Replace("a b","a  b"),pg,true)==Result.Changed,"nested dollar strings retain content");
            Expect(SqlDiff.Classify(a,b,pg,false)==Result.Changed,"non SQL body retains whitespace");
        }

        static void PostgresUiTests(App ui,FrameworkElement host,TabControl tabs,TabItem home,HistoryStore history) {
            tabs.SelectedItem=home;
            ((Button)ui.Window.FindName("NewPostgres")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Expect(ui.Current.Engine==DatabaseEngine.PostgreSql && ui.Current.Get<TextBox>("LeftConn").ToolTip.ToString().Contains("Host="),"PG launcher selects PG connection workflow");
            string admin=Environment.GetEnvironmentVariable("DBCOMPARE_PG_TEST_ADMIN");
            if(String.IsNullOrWhiteSpace(admin)) {ui.Close(ui.Current.Tab);return;}
            // Opt-in integration fixture: only create and drop uniquely named databases and a role on a test server.
            string prefix="dbc_test_"+Guid.NewGuid().ToString("N");
            string left=prefix+"_a",right=prefix+"_b",role=prefix+"_reader";
            using(var control=new NpgsqlConnection(admin)) {
                control.Open();
                Execute(control,"CREATE ROLE "+role);
                try {
                    Execute(control,"CREATE DATABASE "+left+" TEMPLATE template0");
                    try {
                        Execute(control,"CREATE DATABASE "+right+" TEMPLATE template0");
                        try {
                            string ca=CreatePgFixture(admin,left,role,false),cb=CreatePgFixture(admin,right,role,true);
                            var session=ui.Current;
                            session.Get<TextBox>("LeftConn").Text=ca;session.Get<TextBox>("RightConn").Text=cb;
                            var start=session.Get<Button>("Compare");
                            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>!session.Busy);
                            Expect(!session.StatusIsError && session.Total>0,"live PG comparison: "+session.Status);
                            var rows=session.Objects.Items.Cast<Result>().ToList();
                            var table=rows.Single(r=>r.Kind=="表" && r.Name=="\"S\".\"orders\"");
                            var props=table.Left.Props;
                            Expect(table.Status==Result.Changed && props["字段 [amount] / 数据类型"]=="numeric(9,2)" &&
                                table.Right.Props["字段 [amount] / 数据类型"]=="numeric(12,2)" && props["字段 [id] / 标识种子"]=="7","live columns, precision and identity");
                            Expect(props["约束 [ck] / SQL 定义"]!=table.Right.Props["约束 [ck] / SQL 定义"] &&
                                props["约束 [fk] / SQL 定义"]!=table.Right.Props["约束 [fk] / SQL 定义"] &&
                                props["索引 [idx_orders] / SQL 定义"]!=table.Right.Props["索引 [idx_orders] / SQL 定义"],"live FK, check and partial expression index");
                            Expect(rows.Single(r=>r.Kind=="视图").Status==Result.Changed && rows.Single(r=>r.Kind=="物化视图").Status==Result.Same,"live view and materialized view");
                            var overloads=rows.Where(r=>r.Kind=="函数" && r.Left.Name=="calc").ToList();
                            Expect(overloads.Count==2 && overloads.Single(r=>r.Name.EndsWith("(integer)")).Status==Result.Format &&
                                overloads.Single(r=>r.Name.EndsWith("(text)")).Status==Result.Changed,"live overloaded routines and nested literals");
                            Expect(rows.Single(r=>r.Kind=="存储过程").Status==Result.Changed,"live procedure");
                            var triggers=rows.Where(r=>r.Kind=="触发器").ToList();
                            Expect(triggers.Count==2 && triggers.Count(r=>r.Status==Result.Changed)==1 && triggers.Select(r=>r.Key).Distinct().Count()==2,"live same trigger name on two tables");
                            Expect(rows.Single(r=>r.Name=="\"S\".\"parents\"").Status==Result.Same &&
                                rows.Count(r=>r.Status==Result.OnlyL)==1 && rows.Count(r=>r.Status==Result.OnlyR)==1,"business rows ignored and one-sided relations included");
                            session.Open(overloads[0]);Layout(host,"postgres-detail");
                            int skipped;var record=history.List(out skipped).First(h=>h.Engine==DatabaseEngine.PostgreSql);
                            history.Rename(record,"PG integration");
                            var open=ui.OpenStored(record);PumpUntil(()=>open.IsCompleted);open.GetAwaiter().GetResult();
                            Expect(ui.Current.Engine==DatabaseEngine.PostgreSql && ui.Current.Total==rows.Count &&
                                ui.Current.Objects.Items.Cast<Result>().Select(r=>r.Key+r.Status).SequenceEqual(rows.Select(r=>r.Key+r.Status)),"PG renamed archive preserves identity and differences");
                            var rerun=ui.Current.Compare();PumpUntil(()=>rerun.IsCompleted);rerun.GetAwaiter().GetResult();
                            Expect(!ui.Current.StatusIsError && history.List(out skipped).Count(h=>h.Engine==DatabaseEngine.PostgreSql)==2,"archived PG session reconnects with correct driver");
                        } finally {Execute(control,"DROP DATABASE "+right);}
                    } finally {Execute(control,"DROP DATABASE "+left);}
                } finally {Execute(control,"DROP ROLE "+role);}
            }
        }
        static void Execute(NpgsqlConnection c,string sql) {using(var cmd=new NpgsqlCommand(sql,c)) cmd.ExecuteNonQuery();}
        static string CreatePgFixture(string admin,string db,string role,bool right) {
            var settings=new NpgsqlConnectionStringBuilder(admin){Database=db,Pooling=false};
            using(var c=new NpgsqlConnection(settings.ConnectionString)) {
                c.Open();Execute(c,@"CREATE SCHEMA ""S""; SET search_path=""S"",pg_catalog;
CREATE TABLE parents(id int PRIMARY KEY);
CREATE TABLE orders(id bigint GENERATED ALWAYS AS IDENTITY (START WITH 7), buyer int,
 amount numeric(PRECISION,2) DEFAULT 0, label text, CONSTRAINT fk FOREIGN KEY(buyer) REFERENCES parents(id) ON DELETE RULE,
 CONSTRAINT ck CHECK(amount OP 0), UNIQUE(label));
CREATE INDEX idx_orders ON orders(lower(label)) WHERE amount OP 0;
CREATE VIEW v AS SELECT amount + OFFSET_VALUE AS total FROM orders;
CREATE MATERIALIZED VIEW mv AS SELECT id FROM parents WITH NO DATA;
CREATE FUNCTION calc(x integer) RETURNS integer LANGUAGE sql AS $body$SELECT INDENT x+1$body$;
CREATE FUNCTION calc(x text) RETURNS text LANGUAGE sql AS $body$SELECT $x$LITERAL$x$ || x$body$;
CREATE PROCEDURE update_orders() LANGUAGE plpgsql AS $body$BEGIN PERFORM OFFSET_VALUE; END$body$;
CREATE FUNCTION touched() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN RETURN NEW; END$$;
CREATE TRIGGER audit BEFORE INSERT ON orders FOR EACH ROW EXECUTE FUNCTION touched();
CREATE TRIGGER audit BEFORE INSERT ON parents FOR EACH ROW EXECUTE FUNCTION touched();"
                    .Replace("PRECISION",right?"12":"9").Replace("RULE",right?"CASCADE":"NO ACTION").Replace(" OP ",right?" >= ":" > ")
                    .Replace("OFFSET_VALUE",right?"2":"1").Replace("INDENT",right?"\n    ":" ").Replace("LITERAL",right?"a  b":"a b"));
                Execute(c,right?"ALTER TABLE orders DISABLE TRIGGER audit;CREATE TABLE right_only(x int);INSERT INTO parents VALUES(42);":
                    "CREATE TABLE left_only(x int);INSERT INTO parents VALUES(1),(2);");
                Execute(c,"GRANT USAGE ON SCHEMA \"S\" TO "+role);
            }
            settings.Options="-c role="+role+" -c default_transaction_read_only=on";
            return settings.ConnectionString;
        }
    }
}
