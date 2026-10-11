using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace SqlToExcelReporting
{
    public sealed class ProductionRow
    {
        public DateTime Date;
        public int Shift;
        public string Line;
        public int Completed;
        public int FirstPass;
    }

    internal static class Program
    {
        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private static string BasePath { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        private static List<ProductionRow> DemoData()
        {
            var rows = new List<ProductionRow>();
            for (int day = 0; day < 7; day++)
                for (int shift = 1; shift <= 2; shift++)
                    for (int line = 0; line < 3; line++) {
                        int completed = 45 + day * 3 + line * 5 + shift * 2;
                        rows.Add(new ProductionRow { Date = new DateTime(2026,10,5).AddDays(day),
                            Shift = shift, Line = "Line " + (char)('A' + line), Completed = completed,
                            FirstPass = completed - (3 + (day + shift + line) % 6) });
                    }
            return rows;
        }

        private static List<ProductionRow> QuerySql(DateTime from, DateTime to, int shift)
        {
            string connectionString = Environment.GetEnvironmentVariable("SQL_REPORT_CONNECTION");
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Set SQL_REPORT_CONNECTION before using --sql.");
            var rows = new List<ProductionRow>();
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(File.ReadAllText(Path.Combine(BasePath,"ReportQuery.sql")), connection)) {
                command.CommandTimeout = 30;
                command.Parameters.Add("@From", SqlDbType.Date).Value = from;
                command.Parameters.Add("@ToExclusive", SqlDbType.Date).Value = to.AddDays(1);
                command.Parameters.Add("@Shift", SqlDbType.Int).Value = shift;
                connection.Open();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) rows.Add(new ProductionRow {
                        Date = reader.GetDateTime(0).Date, Shift = reader.GetInt32(1),
                        Line = reader.GetString(2), Completed = reader.GetInt32(3), FirstPass = reader.GetInt32(4)
                    });
            }
            return rows;
        }

        private static void Validate(IEnumerable<ProductionRow> rows)
        {
            var data = rows.ToList();
            if (data.Any(r => String.IsNullOrWhiteSpace(r.Line) || r.Line.Length > 80 ||
                r.Shift < 1 || r.Shift > 2 || r.Completed < 0 || r.FirstPass < 0 || r.FirstPass > r.Completed))
                throw new InvalidOperationException("Invalid source row: check line, shift and unit counts.");
            if (data.GroupBy(r => new { r.Date, r.Shift, r.Line }).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Duplicate date/shift/line key in source records.");
        }

        private static XElement Cell(string address, string style, string text, double? number, string formula)
        {
            var cell = new XElement(Ns + "c", new XAttribute("r",address));
            if (!String.IsNullOrEmpty(style)) cell.SetAttributeValue("s",style);
            if (text != null) {
                cell.SetAttributeValue("t","inlineStr");
                cell.Add(new XElement(Ns + "is",new XElement(Ns + "t",text)));
            } else {
                if (formula != null) cell.Add(new XElement(Ns + "f",formula));
                if (number.HasValue) cell.Add(new XElement(Ns + "v",number.Value.ToString("R",Culture)));
            }
            return cell;
        }

        private static string[] Styles(XElement row, int columns)
        {
            return Enumerable.Range(0,columns).Select(i => {
                var cell = row.Elements(Ns+"c").FirstOrDefault(c =>
                    ((string)c.Attribute("r")).StartsWith(((char)('A'+i)).ToString(),StringComparison.Ordinal));
                return cell == null ? null : (string)cell.Attribute("s");
            }).ToArray();
        }

        private static XDocument ReadXml(ZipArchive archive, string path)
        {
            using (var stream = archive.GetEntry(path).Open()) return XDocument.Load(stream);
        }

        private static void ReplaceXml(ZipArchive archive, string path, XDocument doc)
        {
            var old = archive.GetEntry(path);
            if (old != null) old.Delete();
            using (var stream = archive.CreateEntry(path).Open()) doc.Save(stream);
        }

        private static void SetDimension(XDocument document, string reference)
        {
            var dimension=document.Root.Element(Ns+"dimension");
            if(dimension==null) {
                dimension=new XElement(Ns+"dimension");
                var properties=document.Root.Element(Ns+"sheetPr");
                if(properties!=null)properties.AddAfterSelf(dimension);else document.Root.AddFirst(dimension);
            }
            dimension.SetAttributeValue("ref",reference);
        }

        private static void Export(List<ProductionRow> rows, string output, DateTime from, DateTime to, int shift, bool sql)
        {
            Validate(rows);
            output = Path.GetFullPath(output);
            string template = Path.GetFullPath(Path.Combine(BasePath,"ReportTemplate.xlsx"));
            if (String.Equals(output,template,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an output other than ReportTemplate.xlsx.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Stage the completed workbook before replacing an existing output.
            string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.Copy(template,temporary);
                using (var archive = ZipFile.Open(temporary,ZipArchiveMode.Update)) {
                    var summary = ReadXml(archive,"xl/worksheets/sheet1.xml");
                    var detail = ReadXml(archive,"xl/worksheets/sheet2.xml");
                    var summaryRows = summary.Root.Element(Ns+"sheetData");
                    var detailRows = detail.Root.Element(Ns+"sheetData");
                    string[] sourceStyle = Styles(detailRows.Elements(Ns+"row").First(r=>(int)r.Attribute("r")==5),7);
                    string[] summaryStyle = Styles(summaryRows.Elements(Ns+"row").First(r=>(int)r.Attribute("r")==8),5);
                    string[] totalStyle = Styles(summaryRows.Elements(Ns+"row").First(r=>(int)r.Attribute("r")==11),5);
                    detailRows.Elements(Ns+"row").Where(r=>(int)r.Attribute("r")>=5).Remove();
                    summaryRows.Elements(Ns+"row").Where(r=>(int)r.Attribute("r")>=8).Remove();
                    string scope = from.ToString("MMM d, yyyy",Culture)+" to "+to.ToString("MMM d, yyyy",Culture)+
                        " | "+(shift==0 ? "Both shifts" : "Shift "+shift);
                    var metadata = summaryRows.Elements(Ns+"row").First(r=>(int)r.Attribute("r")==3);
                    string metadataStyle = (string)metadata.Elements(Ns+"c").First().Attribute("s");
                    metadata.Elements(Ns+"c").Remove(); metadata.Add(Cell("A3",metadataStyle,scope,null,null));
                    var summarySource = summaryRows.Elements(Ns+"row").First(r=>(int)r.Attribute("r")==4);
                    string summarySourceStyle=(string)summarySource.Elements(Ns+"c").First().Attribute("s");
                    summarySource.Elements(Ns+"c").Remove();
                    summarySource.Add(Cell("A4",summarySourceStyle,sql ? "SQL Server query result" : "Synthetic demonstration data",null,null));
                    var source = detailRows.Elements(Ns+"row").First(r=>(int)r.Attribute("r")==2);
                    string sourceLabelStyle=(string)source.Elements(Ns+"c").First().Attribute("s");
                    source.Elements(Ns+"c").Remove();
                    source.Add(Cell("A2",sourceLabelStyle,sql ? "Source: SQL Server query result" : "Source: synthetic demonstration data",null,null));
                    for (int i=0;i<rows.Count;i++) {
                        var r=rows[i]; int n=i+5;
                        detailRows.Add(new XElement(Ns+"row",new XAttribute("r",n),new XAttribute("ht",24),new XAttribute("customHeight",1),
                            Cell("A"+n,sourceStyle[0],null,r.Date.ToOADate(),null),
                            Cell("B"+n,sourceStyle[1],null,r.Shift,null),Cell("C"+n,sourceStyle[2],r.Line,null,null),
                            Cell("D"+n,sourceStyle[3],null,r.Completed,null),Cell("E"+n,sourceStyle[4],null,r.FirstPass,null),
                            Cell("F"+n,sourceStyle[5],null,r.Completed-r.FirstPass,"D"+n+"-E"+n),
                            Cell("G"+n,sourceStyle[6],null,r.Completed==0 ? (double?)null : (double)r.FirstPass/r.Completed,
                                "IF(D"+n+"=0,\"\",E"+n+"/D"+n+")")));
                    }
                    int end=Math.Max(5,rows.Count+4), number=8;
                    foreach (var group in rows.GroupBy(r=>r.Line).OrderBy(g=>g.Key)) {
                        int completed=group.Sum(r=>r.Completed), passed=group.Sum(r=>r.FirstPass);
                        var row=new XElement(Ns+"row",new XAttribute("r",number),new XAttribute("ht",28),new XAttribute("customHeight",1));
                        row.Add(Cell("A"+number,summaryStyle[0],group.Key,null,null));
                        for(int col=1;col<=3;col++) {
                            char detailCol=(char)('C'+col), summaryCol=(char)('A'+col);
                            double count=col==1 ? completed : col==2 ? passed : completed-passed;
                            row.Add(Cell(summaryCol.ToString()+number,summaryStyle[col],null,count,
                                "SUMIFS('Detail'!$"+detailCol+"$5:$"+detailCol+"$"+end+",'Detail'!$C$5:$C$"+end+",A"+number+")"));
                        }
                        row.Add(Cell("E"+number,summaryStyle[4],null,completed==0 ? (double?)null : (double)passed/completed,
                            "IF(B"+number+"=0,\"\",C"+number+"/B"+number+")"));
                        summaryRows.Add(row); number++;
                    }
                    int totalCompleted=rows.Sum(r=>r.Completed), totalPassed=rows.Sum(r=>r.FirstPass);
                    var total=new XElement(Ns+"row",new XAttribute("r",number),new XAttribute("ht",28),new XAttribute("customHeight",1));
                    total.Add(Cell("A"+number,totalStyle[0],"TOTAL",null,null));
                    for(int col=1;col<=3;col++) {
                        char c=(char)('A'+col);
                        total.Add(Cell(c.ToString()+number,totalStyle[col],null,col==1 ? totalCompleted : col==2 ? totalPassed : totalCompleted-totalPassed,
                            number==8 ? "0" : "SUM("+c+"8:"+c+(number-1)+")"));
                    }
                    total.Add(Cell("E"+number,totalStyle[4],null,totalCompleted==0 ? (double?)null : (double)totalPassed/totalCompleted,
                        "IF(B"+number+"=0,\"\",C"+number+"/B"+number+")"));
                    summaryRows.Add(total);
                    summaryRows.Add(new XElement(Ns+"row",new XAttribute("r",number+3),
                        Cell("A"+(number+3),null,"First pass yield = first-pass units / completed units. Rates are weighted by volume.",null,null)));
                    SetDimension(summary,"A1:H"+(number+3));
                    SetDimension(detail,"A1:G"+end);
                    var filter=detail.Root.Element(Ns+"autoFilter");
                    if(filter!=null)filter.SetAttributeValue("ref","A4:G"+end);
                    ReplaceXml(archive,"xl/worksheets/sheet1.xml",summary);
                    ReplaceXml(archive,"xl/worksheets/sheet2.xml",detail);
                    var workbook=ReadXml(archive,"xl/workbook.xml");
                    var calculation=workbook.Root.Element(Ns+"calcPr");
                    if(calculation==null){calculation=new XElement(Ns+"calcPr");workbook.Root.Add(calculation);}
                    calculation.SetAttributeValue("calcMode","auto");calculation.SetAttributeValue("fullCalcOnLoad",1);
                    ReplaceXml(archive,"xl/workbook.xml",workbook);
                }
                if(File.Exists(output))File.Replace(temporary,output,null);else File.Move(temporary,output);
            } finally {if(File.Exists(temporary))File.Delete(temporary);}
        }

        private static string Option(string[] args,string name,string fallback)
        {
            int i=Array.IndexOf(args,name);
            if(i<0)return fallback;if(i+1>=args.Length)throw new ArgumentException("Missing value for "+name);
            return args[i+1];
        }

        private static int Main(string[] args)
        {
            try {
                var from=DateTime.ParseExact(Option(args,"--from","2026-10-05"),"yyyy-MM-dd",Culture);
                var to=DateTime.ParseExact(Option(args,"--to","2026-10-11"),"yyyy-MM-dd",Culture);
                int shift=Int32.Parse(Option(args,"--shift","0"));
                if(to<from||shift<0||shift>2)throw new ArgumentException("Check date range and shift (0, 1 or 2).");
                bool sql=args.Contains("--sql");
                var rows=sql ? QuerySql(from,to,shift) : DemoData().Where(r=>r.Date>=from&&r.Date<=to&&(shift==0||r.Shift==shift)).ToList();
                rows=rows.OrderBy(r=>r.Date).ThenBy(r=>r.Shift).ThenBy(r=>r.Line).ToList();Validate(rows);
                if(args.Contains("--self-test")) {
                    if(rows.Count!=42||rows.Sum(r=>r.Completed)!=2604||rows.Sum(r=>r.FirstPass)!=2373)
                        throw new InvalidOperationException("Default sample totals changed.");
                    bool rejected=false;
                    try {Validate(rows.Concat(new[]{rows[0]}));} catch(InvalidOperationException){rejected=true;}
                    if(!rejected)throw new InvalidOperationException("Duplicate check failed.");
                    Console.WriteLine("PASS: sample totals and duplicate-key rejection.");return 0;
                }
                if(args.Contains("--json")) {
                    var data=rows.Select(r=>new { date=r.Date.ToString("yyyy-MM-dd"),shift=r.Shift,line=r.Line,completed=r.Completed,firstPass=r.FirstPass }).ToArray();
                    File.WriteAllText(Option(args,"--json","SampleData.json"),new JavaScriptSerializer().Serialize(data));return 0;
                }
                string output=Option(args,"--output",Path.Combine(BasePath,"Weekly_Report.xlsx"));
                Export(rows,output,from,to,shift,sql);
                Console.WriteLine("Created "+Path.GetFullPath(output)+" from "+rows.Count+" records ("+(sql?"SQL Server":"synthetic demo data")+").");return 0;
            } catch(SqlException) {Console.Error.WriteLine("SQL query failed. Check connection, permissions and the demo schema.");return 1;}
              catch(Exception ex) {Console.Error.WriteLine(ex.GetType().Name+": "+(args.Contains("--sql") ? "Check SQL settings, source schema and output path." : ex.Message));return 1;}
        }
    }
}
