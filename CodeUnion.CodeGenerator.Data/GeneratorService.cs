using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Text;
using System.Data;
using System.IO;
using System.Linq;
using Jd.Utility;
//using System.Data.OleDb;


namespace CodeUnion.CodeGenerator.Data
{
    public class GeneratorService : IGeneratorService
    {
        /// <summary>
        /// 根据模板生成文件
        /// </summary>
        /// <param name="tables"></param>
        /// <param name="parameters"></param>
        public void Generate(IList<Table> tables, IDictionary<string, object> parameters)
        {
            foreach (Table table in tables)
            {
                IDictionary<string, object> args = new Dictionary<string, object>(parameters);
                args.Add("table", table);
                args.Add("allColumns", table.AllColumns);
                args.Add("keys", table.Keys);
                args.Add("columns", table.Columns);
                args.Add("key", table.Keys.FirstOrDefault<Column>());
                string[] templateFiles = Directory.GetFiles(this.TemplatePath, "*.vm", System.IO.SearchOption.TopDirectoryOnly);
                foreach (string templateFile in templateFiles)
                {
                    string templateFileName = Path.GetFileName(templateFile);
                    string path = this.OutputPath;
                    switch (templateFileName)
                    {
                        case "IModelService.cs.vm":
                            path = Path.Combine(this.OutputPath, "Contract");
                            break;
                        case "Model.cs.vm":
                            path = Path.Combine(this.OutputPath, "Model");
                            break;
                        case "ModelDao.cs.vm":
                            path = Path.Combine(this.OutputPath, "Dao");
                            break;
                        case "ModelDao.xml.vm":
                            path = Path.Combine(this.OutputPath, "Dao");
                            break;
                        case "ModelService.cs.vm":
                            path = Path.Combine(this.OutputPath, "Service");
                            break;
                        case "PaginationBase.cs.vm":
                            path = Path.Combine(this.OutputPath, "Model");
                            break;
                        case "QPBase.cs.vm":
                            path = Path.Combine(this.OutputPath, "Model");
                            break;
                        default:
                            break;
                    }
                    TextWriteOverride(Path.Combine(path, templateFileName.Replace("Model", table.Alias)).Replace(".vm", ""), TemplateUtility.ParseVelocity(this.TemplatePath, templateFileName, args));
                }
            }
        }

        /// <summary>
        /// 将一个字符串写入指定的文本[覆盖原有数据]（如果文本|路径存在则创建文本|路径）
        /// </summary>
        /// <param name="path">指定路径</param>
        /// <param name="row">字符串</param>
        /// <returns>成功返回true</returns>
        public static bool TextWriteOverride(string path, string row)
        {
            try
            {
                string path1 = path.Substring(0, path.LastIndexOf("\\"));
                if (!Directory.Exists(path1))
                {
                    Directory.CreateDirectory(path1);
                }
                path1 = null;

                FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
                StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
                streamWriter.WriteLine(row);
                streamWriter.Close();
                fileStream.Close();
            }
            catch
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 获取所有的数据列
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public IList<Column> GetColumns(string tableName)
        {
            IList<Column> result = new List<Column>();
            IList<string> primaryKeys = this.GetPrimaryKeys(tableName);
            string[] strings = new string[3];
            strings[2] = tableName;
            DataTable columns = this.GetSchema("Columns", strings);

            DataTable temp = RunExecute_Sql("SELECT a.name AS CName,isnull(g.[value],'') AS CDescription FROM sys.columns a left join sys.extended_properties g on (a.object_id = g.major_id AND g.minor_id = a.column_id) WHERE object_id = (SELECT object_id FROM sys.tables WHERE name = '" + tableName + "')");

            foreach (DataRow row in columns.Rows)
            {
                Column column = new Column();
                column.Name = row.Field<string>("Column_Name");
                column.Type = this.GetDataType(row.Field<int>("Data_Type"));

                string desc = column.Name;
                foreach (DataRow trow in temp.Rows)
                {
                    if (trow.Field<string>("CName") == column.Name)
                    {
                        desc = trow.Field<string>("CDescription");
                        break;
                    }
                }
                if (string.IsNullOrEmpty(desc))
                {
                    desc = column.Name;
                }
                column.Description = desc;

                //column.Description = row.Field<string>("Description");

                column.Length = row.Field<long?>("Character_Maximum_Length");
                column.Nullable = row.Field<bool>("Is_Nullable");
                column.DefaultValue = row.Field<string>("Column_Default");
                column.Sequence = row.Field<long>("Ordinal_Position");
                column.Type = this.GetCSharpType(column);
                column.Primary = primaryKeys.Contains(column.Name);
                result.Add(column);
            }
            if (result.Count > 0)
            {
                result = new List<Column>(result.OrderBy<Column, string>(c => c.Name));
            }
            return result;
        }

        /// <summary>
        /// 获取对应的C#类型
        /// </summary>
        /// <param name="column"></param>
        /// <returns></returns>
        private string GetCSharpType(Column column)
        {
            switch (column.Type)
            {
                case "System.Boolean":
                    return "bool";

                case "System.Byte":
                    return "byte";

                case "System.DateTime":
                    return "DateTime";

                case "System.Decimal":
                    return "decimal";

                case "System.Int32":
                    return "int";

                case "System.String":
                    return "string";
            }
            return column.Type;
        }

        /// <summary>
        /// 获取数据列类型
        /// </summary>
        /// <param name="nativeType"></param>
        /// <returns></returns>
        private string GetDataType(int nativeType)
        {
            string result = "";
            DataTable dataTypes = this.GetSchema("DataTypes", null);
            if (dataTypes.Rows.Count > 0)
            {
                DataRow[] types = dataTypes.Select(string.Format("NativeDataType = {0}", nativeType));
                if (types.Length > 0)
                {
                    result = types[0].Field<string>("DataType");
                }
            }
            return result;
        }

        /// <summary>
        /// 获取主键
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public IList<string> GetPrimaryKeys(string tableName)
        {
            IList<string> result = new List<string>();
            string[] tableNames = new string[5];
            tableNames[4] = tableName;
            DataTable indexes = this.GetSchema("Indexes", tableNames);
            if (indexes.Rows.Count > 0)
            {
                DataRow[] keys = indexes.Select("Primary_Key = true");
                foreach (DataRow key in keys)
                {
                    result.Add(key.Field<string>("Column_Name"));
                }
            }
            return result;
        }

        /// <summary>
        /// 获取指定的数据库架构信息
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="restrictionValues"></param>
        /// <returns></returns>
        public DataTable GetSchema(string collectionName, string[] restrictionValues)
        {
            DataTable result = null;
            using (OleDbConnection connection = new OleDbConnection(this.ConnectionString))
            {
                connection.Open();
                switch (collectionName)
                {
                    case "Columns":
                        result = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, restrictionValues);
                        break;
                    default:
                        result = connection.GetSchema(collectionName, restrictionValues);
                        break;
                }
                connection.Close();
            }
            return result;
        }

        /// <summary>
        /// 根据查询条件获取DataTable
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <returns></returns>
        public DataTable RunExecute_Sql(string sql)
        {
            DataTable Result = null;
            try
            {
                string constr = string.Empty;
                string[] temp = ConnectionString.Split(';');
                foreach (var s in temp)
                {
                    string[] strs = s.Split('=');
                    switch (strs[0])
                    {
                        case "data source":
                            constr += "server=" + strs[1] + ";";
                            break;
                        case "User ID":
                            constr += "uid=" + strs[1] + ";";
                            break;
                        case "pwd":
                            constr += "pwd=" + strs[1] + ";";
                            break;
                        case "Initial Catalog":
                            constr += "database=" + strs[1] + ";";
                            break;
                    }
                }
                constr += "Connection Timeout=10";
                using (SqlConnection Conn = new SqlConnection(constr))
                {
                    Conn.Open();
                    SqlDataAdapter adp = new SqlDataAdapter(sql, Conn);
                    DataSet ds = new DataSet();
                    adp.Fill(ds);
                    Result = ds.Tables[0];
                    Conn.Close();
                }
            }
            catch
            {
                Result = null;
            }
            return Result;
        }

        /// <summary>
        /// 获取所有的用户表
        /// </summary>
        /// <param name="schemaType"></param>
        /// <returns></returns>
        public IList<Table> GetTables(string schemaType)
        {
            IList<Table> result = new List<Table>();

            string[] strings = new string[4];
            strings[3] = schemaType;
            DataTable tables = this.GetSchema("Tables", strings);

            DataTable temp =
                RunExecute_Sql(
                    "select a.name AS TName,isnull(g.[value],'') AS TDescription from sys.tables a left join sys.extended_properties g on (a.object_id = g.major_id AND g.minor_id = 0)");
            foreach (DataRow row in tables.Rows)
            {
                Table table = new Table();
                table.Name = row.Field<string>("Table_Name");
                string desc = table.Name;
                try
                {
                    foreach (DataRow trow in temp.Rows)
                    {
                        if (trow.Field<string>("TName") == table.Name)
                        {
                            desc = trow.Field<string>("TDescription");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Todo: handler exception
                }

                if (string.IsNullOrEmpty(desc))
                {
                    desc = table.Name;
                }

                table.Description = desc;

                table.Owner = row.Field<string>("Table_Schema");
                table.CreationTime = row.Field<DateTime>("Date_Created");
                table.ModificationTime = row.Field<DateTime?>("Date_Modified");

                table.AllColumns = this.GetColumns(table.Name);
                IEnumerable<Column> keys = table.AllColumns.Where<Column>(c => c.Primary);
                table.Keys = new List<Column>(keys);
                IEnumerable<Column> columns = table.AllColumns.Where<Column>(c => !c.Primary);
                table.Columns = new List<Column>(columns);
                result.Add(table);
            }

            return result;

        }

        public string ConnectionString
        {
            get;
            set;
        }



        public string OutputPath
        {
            get;
            set;
        }

        public string TemplatePath
        {
            get;
            set;
        }
    }
}
