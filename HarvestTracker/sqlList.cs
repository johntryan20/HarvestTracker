using System;
using sqlList;

namespace HarvestTracker
{
	class sqlList
	{
		1 // 
		2 // Copyright (c) 2009-2014 Krueger Systems, Inc. 
		3 //  
		4 // Permission is hereby granted, free of charge, to any person obtaining a copy 
		5 // of this software and associated documentation files (the "Software"), to deal 
		6 // in the Software without restriction, including without limitation the rights 
		7 // to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
		8 // copies of the Software, and to permit persons to whom the Software is 
		9 // furnished to do so, subject to the following conditions: 
		10 //  
		11 // The above copyright notice and this permission notice shall be included in 
		12 // all copies or substantial portions of the Software. 
		13 //  
		14 // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
		15 // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
		16 // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
		17 // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
		18 // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
		19 // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
		20 // THE SOFTWARE. 
		21 // 
		22 #if WINDOWS_PHONE && !USE_WP8_NATIVE_SQLITE 
			23 #define USE_CSHARP_SQLITE 
			24 #endif 
			25 

		26 #if NETFX_CORE 
			27 #define USE_NEW_REFLECTION_API 
			28 #endif 
			29 

			30 using System; 
		31 using System.Diagnostics; 
		32 #if !USE_SQLITEPCL_RAW 
			33 using System.Runtime.InteropServices; 
		34 #endif 
		35 using System.Collections.Generic; 
		36 using System.Collections.Concurrent; 
		37 using System.Reflection; 
		38 using System.Linq; 
		39 using System.Linq.Expressions; 
		40 using System.Threading; 
		41 

		42 #if USE_CSHARP_SQLITE 
			43 using Sqlite3 = Community.CsharpSqlite.Sqlite3; 
		44 using Sqlite3DatabaseHandle = Community.CsharpSqlite.Sqlite3.sqlite3; 
		45 using Sqlite3Statement = Community.CsharpSqlite.Sqlite3.Vdbe; 
		46 #elif USE_WP8_NATIVE_SQLITE 
		47 using Sqlite3 = Sqlite.Sqlite3; 
		48 using Sqlite3DatabaseHandle = Sqlite.Database; 
		49 using Sqlite3Statement = Sqlite.Statement; 
		50 #elif USE_SQLITEPCL_RAW 
		51 using Sqlite3DatabaseHandle = SQLitePCL.sqlite3; 
		52 using Sqlite3Statement = SQLitePCL.sqlite3_stmt; 
		53 using Sqlite3 = SQLitePCL.raw; 
		54 #else 
			55 using Sqlite3DatabaseHandle = System.IntPtr; 
		56 using Sqlite3Statement = System.IntPtr; 
		57 #endif 
		58 

		59 namespace SQLite 
		60 { 
			61 	public class SQLiteException : Exception 
			62 	{ 
				63 		public SQLite3.Result Result { get; private set; } 
				64 

				65 		protected SQLiteException (SQLite3.Result r,string message) : base(message) 
				66 		{ 
					67 			Result = r; 
					68 		} 
				69 

				70 		public static SQLiteException New (SQLite3.Result r, string message) 
				71 		{ 
					72 			return new SQLiteException (r, message); 
					73 		} 
				74 	} 
			75 

			76 	public class NotNullConstraintViolationException : SQLiteException 
			77 	{ 
				78 		public IEnumerable<TableMapping.Column> Columns { get; protected set; } 
				79 

				80 		protected NotNullConstraintViolationException (SQLite3.Result r, string message) 
				81 			: this (r, message, null, null) 
				82 		{ 
					83 

					84 		} 
				85 

				86 		protected NotNullConstraintViolationException (SQLite3.Result r, string message, TableMapping mapping, object obj) 
				87 			: base (r, message) 
				88 		{ 
					89 			if (mapping != null && obj != null) { 
						90 				this.Columns = from c in mapping.Columns 
								91 							   where c.IsNullable == false && c.GetValue (obj) == null 
							92 							   select c; 
						93 			} 
					94 		} 
				95 

				96 		public static new NotNullConstraintViolationException New (SQLite3.Result r, string message) 
				97 		{ 
					98 			return new NotNullConstraintViolationException (r, message); 
					99 		} 
				100 

				101 		public static NotNullConstraintViolationException New (SQLite3.Result r, string message, TableMapping mapping, object obj) 
				102 		{ 
					103 			return new NotNullConstraintViolationException (r, message, mapping, obj); 
					104 		} 
				105 

				106 		public static NotNullConstraintViolationException New (SQLiteException exception, TableMapping mapping, object obj) 
				107 		{ 
					108 			return new NotNullConstraintViolationException (exception.Result, exception.Message, mapping, obj); 
					109 		} 
				110 	} 
			111 

			112 	[Flags] 
			113 	public enum SQLiteOpenFlags { 
				114 		ReadOnly = 1, ReadWrite = 2, Create = 4, 
				115 		NoMutex = 0x8000, FullMutex = 0x10000, 
				116 		SharedCache = 0x20000, PrivateCache = 0x40000, 
				117 		ProtectionComplete = 0x00100000, 
				118 		ProtectionCompleteUnlessOpen = 0x00200000, 
				119 		ProtectionCompleteUntilFirstUserAuthentication = 0x00300000, 
				120 		ProtectionNone = 0x00400000 
					121 	} 
			122 

			123     [Flags] 
			124     public enum CreateFlags 
			125     { 
				126         None = 0, 
				127         ImplicitPK = 1,    // create a primary key for field called 'Id' (Orm.ImplicitPkName) 
				128         ImplicitIndex = 2, // create an index for fields ending in 'Id' (Orm.ImplicitIndexSuffix) 
				129         AllImplicit = 3,   // do both above 
				130 

				131         AutoIncPK = 4      // force PK field to be auto inc 
					132     } 
			133 

			134 	/// <summary> 
			135 	/// Represents an open connection to a SQLite database. 
			136 	/// </summary> 
			137 	public partial class SQLiteConnection : IDisposable 
			138 	{ 
				139 		private bool _open; 
				140 		private TimeSpan _busyTimeout; 
				141 		private Dictionary<string, TableMapping> _mappings = null; 
				142 		private Dictionary<string, TableMapping> _tables = null; 
				143 		private System.Diagnostics.Stopwatch _sw; 
				144 		private long _elapsedMilliseconds = 0; 
				145 

				146 		private int _transactionDepth = 0; 
				147 		private Random _rand = new Random (); 
				148 

				149 		public Sqlite3DatabaseHandle Handle { get; private set; } 
				150 		internal static readonly Sqlite3DatabaseHandle NullHandle = default(Sqlite3DatabaseHandle); 
				151 

				152 		public string DatabasePath { get; private set; } 
				153 

				154 		public bool TimeExecution { get; set; } 
				155 

				156 		public bool Trace { get; set; } 
				157 

				158 		public bool StoreDateTimeAsTicks { get; private set; } 
				159 

				160 		/// <summary> 
				161 		/// Constructs a new SQLiteConnection and opens a SQLite database specified by databasePath. 
				162 		/// </summary> 
				163 		/// <param name="databasePath"> 
				164 		/// Specifies the path to the database file. 
				165 		/// </param> 
				166 		/// <param name="storeDateTimeAsTicks"> 
				167 		/// Specifies whether to store DateTime properties as ticks (true) or strings (false). You 
				168 		/// absolutely do want to store them as Ticks in all new projects. The default of false is 
				169 		/// only here for backwards compatibility. There is a *significant* speed advantage, with no 
				170 		/// down sides, when setting storeDateTimeAsTicks = true. 
				171 		/// </param> 
				172 		public SQLiteConnection (string databasePath, bool storeDateTimeAsTicks = false) 
				173 			: this (databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create, storeDateTimeAsTicks) 
				174 		{ 
					175 		} 
				176 

				177 		/// <summary> 
				178 		/// Constructs a new SQLiteConnection and opens a SQLite database specified by databasePath. 
				179 		/// </summary> 
				180 		/// <param name="databasePath"> 
				181 		/// Specifies the path to the database file. 
				182 		/// </param> 
				183 		/// <param name="storeDateTimeAsTicks"> 
				184 		/// Specifies whether to store DateTime properties as ticks (true) or strings (false). You 
				185 		/// absolutely do want to store them as Ticks in all new projects. The default of false is 
				186 		/// only here for backwards compatibility. There is a *significant* speed advantage, with no 
				187 		/// down sides, when setting storeDateTimeAsTicks = true. 
				188 		/// </param> 
				189 		public SQLiteConnection (string databasePath, SQLiteOpenFlags openFlags, bool storeDateTimeAsTicks = false) 
				190 		{ 
					191 			if (string.IsNullOrEmpty (databasePath)) 
						192 				throw new ArgumentException ("Must be specified", "databasePath"); 
					193 

					194 			DatabasePath = databasePath; 
					195 

					196 #if NETFX_CORE 
						197 			SQLite3.SetDirectory(/*temp directory type*/2, Windows.Storage.ApplicationData.Current.TemporaryFolder.Path); 
					198 #endif 
					199 

					200 			Sqlite3DatabaseHandle handle; 
					201 

					202 #if SILVERLIGHT || USE_CSHARP_SQLITE || USE_SQLITEPCL_RAW 
						203             var r = SQLite3.Open (databasePath, out handle, (int)openFlags, IntPtr.Zero); 
					204 #else 
						205 			// open using the byte[] 
						206 			// in the case where the path may include Unicode 
						207 			// force open to using UTF-8 using sqlite3_open_v2 
						208 			var databasePathAsBytes = GetNullTerminatedUtf8 (DatabasePath); 
					209 			var r = SQLite3.Open (databasePathAsBytes, out handle, (int) openFlags, IntPtr.Zero); 
					210 #endif 
					211 

					212 			Handle = handle; 
					213 			if (r != SQLite3.Result.OK) { 
						214 				throw SQLiteException.New (r, String.Format ("Could not open database file: {0} ({1})", DatabasePath, r)); 
						215 			} 
					216 			_open = true; 
					217 

					218 			StoreDateTimeAsTicks = storeDateTimeAsTicks; 
					219 			 
					220 			BusyTimeout = TimeSpan.FromSeconds (0.1); 
					221 		} 
				222 		 
				223 #if __IOS__ 
					224 		static SQLiteConnection () 
					225 		{ 
					226 			if (_preserveDuringLinkMagic) { 
						227 				var ti = new ColumnInfo (); 
						228 				ti.Name = "magic"; 
						229 			} 
					230 		} 
				231 

				232    		/// <summary> 
				233 		/// Used to list some code that we want the MonoTouch linker 
				234 		/// to see, but that we never want to actually execute. 
				235 		/// </summary> 
				236 		static bool _preserveDuringLinkMagic; 
				237 #endif 
				238 

				239 #if !USE_SQLITEPCL_RAW 
					240         public void EnableLoadExtension(int onoff) 
					241         { 
					242             SQLite3.Result r = SQLite3.EnableLoadExtension(Handle, onoff); 
					243 			if (r != SQLite3.Result.OK) { 
						244 				string msg = SQLite3.GetErrmsg (Handle); 
						245 				throw SQLiteException.New (r, msg); 
						246 			} 
					247         } 
				248 #endif 
				249 

				250 #if !USE_SQLITEPCL_RAW 
					251 		static byte[] GetNullTerminatedUtf8 (string s) 
					252 		{ 
					253 			var utf8Length = System.Text.Encoding.UTF8.GetByteCount (s); 
					254 			var bytes = new byte [utf8Length + 1]; 
					255 			utf8Length = System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0); 
					256 			return bytes; 
					257 		} 
				258 #endif 
				259 

				260         /// <summary> 
				261 		/// Sets a busy handler to sleep the specified amount of time when a table is locked. 
				262 		/// The handler will sleep multiple times until a total time of <see cref="BusyTimeout"/> has accumulated. 
				263 		/// </summary> 
				264 		public TimeSpan BusyTimeout { 
					265 			get { return _busyTimeout; } 
					266 			set { 
						267 				_busyTimeout = value; 
						268 				if (Handle != NullHandle) { 
							269 					SQLite3.BusyTimeout (Handle, (int)_busyTimeout.TotalMilliseconds); 
							270 				} 
						271 			} 
					272 		} 
				273 

				274 		/// <summary> 
				275 		/// Returns the mappings from types to tables that the connection 
				276 		/// currently understands. 
				277 		/// </summary> 
				278 		public IEnumerable<TableMapping> TableMappings { 
					279 			get { 
						280 				return _tables != null ? _tables.Values : Enumerable.Empty<TableMapping> (); 
						281 			} 
					282 		} 
				283 

				284 		/// <summary> 
				285 		/// Retrieves the mapping that is automatically generated for the given type. 
				286 		/// </summary> 
				287 		/// <param name="type"> 
				288 		/// The type whose mapping to the database is returned. 
				289 		/// </param>          
				290         /// <param name="createFlags"> 
				291 		/// Optional flags allowing implicit PK and indexes based on naming conventions 
				292 		/// </param>      
				293 		/// <returns> 
				294 		/// The mapping represents the schema of the columns of the database and contains  
				295 		/// methods to set and get properties of objects. 
				296 		/// </returns> 
				297         public TableMapping GetMapping(Type type, CreateFlags createFlags = CreateFlags.None) 
				298 		{ 
					299 			if (_mappings == null) { 
						300 				_mappings = new Dictionary<string, TableMapping> (); 
						301 			} 
					302 			TableMapping map; 
					303 			if (!_mappings.TryGetValue (type.FullName, out map)) { 
						304 				map = new TableMapping (type, createFlags); 
						305 				_mappings [type.FullName] = map; 
						306 			} 
					307 			return map; 
					308 		} 
				309 		 
				310 		/// <summary> 
				311 		/// Retrieves the mapping that is automatically generated for the given type. 
				312 		/// </summary> 
				313 		/// <returns> 
				314 		/// The mapping represents the schema of the columns of the database and contains  
				315 		/// methods to set and get properties of objects. 
				316 		/// </returns> 
				317 		public TableMapping GetMapping<T> () 
				318 		{ 
					319 			return GetMapping (typeof (T)); 
					320 		} 
				321 

				322 		private struct IndexedColumn 
				323 		{ 
					324 			public int Order; 
					325 			public string ColumnName; 
					326 		} 
				327 

				328 		private struct IndexInfo 
				329 		{ 
					330 			public string IndexName; 
					331 			public string TableName; 
					332 			public bool Unique; 
					333 			public List<IndexedColumn> Columns; 
					334 		} 
				335 

				336 		/// <summary> 
				337 		/// Executes a "drop table" on the database.  This is non-recoverable. 
				338 		/// </summary> 
				339 		public int DropTable<T>() 
				340 		{ 
					341 			var map = GetMapping (typeof (T)); 
					342 

					343 			var query = string.Format("drop table if exists \"{0}\"", map.TableName); 
					344 

					345 			return Execute (query); 
					346 		} 
				347 		 
				348 		/// <summary> 
				349 		/// Executes a "create table if not exists" on the database. It also 
				350 		/// creates any specified indexes on the columns of the table. It uses 
				351 		/// a schema automatically generated from the specified type. You can 
				352 		/// later access this schema by calling GetMapping. 
				353 		/// </summary> 
				354 		/// <returns> 
				355 		/// The number of entries added to the database schema. 
				356 		/// </returns> 
				357 		public int CreateTable<T>(CreateFlags createFlags = CreateFlags.None) 
				358 		{ 
					359 			return CreateTable(typeof (T), createFlags); 
					360 		} 
				361 

				362 		/// <summary> 
				363 		/// Executes a "create table if not exists" on the database. It also 
				364 		/// creates any specified indexes on the columns of the table. It uses 
				365 		/// a schema automatically generated from the specified type. You can 
				366 		/// later access this schema by calling GetMapping. 
				367 		/// </summary> 
				368 		/// <param name="ty">Type to reflect to a database table.</param> 
				369         /// <param name="createFlags">Optional flags allowing implicit PK and indexes based on naming conventions.</param>   
				370 		/// <returns> 
				371 		/// The number of entries added to the database schema. 
				372 		/// </returns> 
				373         public int CreateTable(Type ty, CreateFlags createFlags = CreateFlags.None) 
				374 		{ 
					375 			if (_tables == null) { 
						376 				_tables = new Dictionary<string, TableMapping> (); 
						377 			} 
					378 			TableMapping map; 
					379 			if (!_tables.TryGetValue (ty.FullName, out map)) { 
						380 				map = GetMapping (ty, createFlags); 
						381 				_tables.Add (ty.FullName, map); 
						382 			} 
					383 			var query = "create table if not exists \"" + map.TableName + "\"(\n"; 
					384 			 
					385 			var decls = map.Columns.Select (p => Orm.SqlDecl (p, StoreDateTimeAsTicks)); 
					386 			var decl = string.Join (",\n", decls.ToArray ()); 
					387 			query += decl; 
					388 			query += ")"; 
					389 			 
					390 			var count = Execute (query); 
					391 			 
					392 			if (count == 0) { //Possible bug: This always seems to return 0? 
						393 				// Table already exists, migrate it 
						394 				MigrateTable (map); 
						395 			} 
					396 

					397 			var indexes = new Dictionary<string, IndexInfo> (); 
					398 			foreach (var c in map.Columns) { 
						399 				foreach (var i in c.Indices) { 
							400 					var iname = i.Name ?? map.TableName + "_" + c.Name; 
							401 					IndexInfo iinfo; 
							402 					if (!indexes.TryGetValue (iname, out iinfo)) { 
								403 						iinfo = new IndexInfo { 
									404 							IndexName = iname, 
									405 							TableName = map.TableName, 
									406 							Unique = i.Unique, 
									407 							Columns = new List<IndexedColumn> () 
										408 						}; 
								409 						indexes.Add (iname, iinfo); 
								410 					} 
							411 

							412 					if (i.Unique != iinfo.Unique) 
								413 						throw new Exception ("All the columns in an index must have the same value for their Unique property"); 
							414 

							415 					iinfo.Columns.Add (new IndexedColumn { 
								416 						Order = i.Order, 
								417 						ColumnName = c.Name 
									418 					}); 
							419 				} 
						420 			} 
					421 

					422 			foreach (var indexName in indexes.Keys) { 
						423 				var index = indexes[indexName]; 
						424 				var columns = index.Columns.OrderBy(i => i.Order).Select(i => i.ColumnName).ToArray(); 
						425                 count += CreateIndex(indexName, index.TableName, columns, index.Unique); 
						426 			} 
					427 			 
					428 			return count; 
					429 		} 
				430 

				431         /// <summary> 
				432         /// Creates an index for the specified table and columns. 
				433         /// </summary> 
				434         /// <param name="indexName">Name of the index to create</param> 
				435         /// <param name="tableName">Name of the database table</param> 
				436         /// <param name="columnNames">An array of column names to index</param> 
				437         /// <param name="unique">Whether the index should be unique</param> 
				438         public int CreateIndex(string indexName, string tableName, string[] columnNames, bool unique = false) 
				439         { 
					440             const string sqlFormat = "create {2} index if not exists \"{3}\" on \"{0}\"(\"{1}\")"; 
					441             var sql = String.Format(sqlFormat, tableName, string.Join ("\", \"", columnNames), unique ? "unique" : "", indexName); 
					442             return Execute(sql); 
					443         } 
				444 

				445         /// <summary> 
				446         /// Creates an index for the specified table and column. 
				447         /// </summary> 
				448         /// <param name="indexName">Name of the index to create</param> 
				449         /// <param name="tableName">Name of the database table</param> 
				450         /// <param name="columnName">Name of the column to index</param> 
				451         /// <param name="unique">Whether the index should be unique</param> 
				452         public int CreateIndex(string indexName, string tableName, string columnName, bool unique = false) 
				453         { 
					454             return CreateIndex(indexName, tableName, new string[] { columnName }, unique); 
					455         } 
				456          
				457         /// <summary> 
				458         /// Creates an index for the specified table and column. 
				459         /// </summary> 
				460         /// <param name="tableName">Name of the database table</param> 
				461         /// <param name="columnName">Name of the column to index</param> 
				462         /// <param name="unique">Whether the index should be unique</param> 
				463         public int CreateIndex(string tableName, string columnName, bool unique = false) 
				464         { 
					465             return CreateIndex(tableName + "_" + columnName, tableName, columnName, unique); 
					466         } 
				467 

				468         /// <summary> 
				469         /// Creates an index for the specified table and columns. 
				470         /// </summary> 
				471         /// <param name="tableName">Name of the database table</param> 
				472         /// <param name="columnNames">An array of column names to index</param> 
				473         /// <param name="unique">Whether the index should be unique</param> 
				474         public int CreateIndex(string tableName, string[] columnNames, bool unique = false) 
				475         { 
					476             return CreateIndex(tableName + "_" + string.Join ("_", columnNames), tableName, columnNames, unique); 
					477         } 
				478 

				479         /// <summary> 
				480         /// Creates an index for the specified object property. 
				481         /// e.g. CreateIndex<Client>(c => c.Name); 
				482         /// </summary> 
				483         /// <typeparam name="T">Type to reflect to a database table.</typeparam> 
				484         /// <param name="property">Property to index</param> 
				485         /// <param name="unique">Whether the index should be unique</param> 
				486         public void CreateIndex<T>(Expression<Func<T, object>> property, bool unique = false) 
				487         { 
					488             MemberExpression mx; 
					489             if (property.Body.NodeType == ExpressionType.Convert) 
						490             { 
						491                 mx = ((UnaryExpression)property.Body).Operand as MemberExpression; 
						492             } 
					493             else 
						494             { 
						495                 mx= (property.Body as MemberExpression); 
						496             } 
					497             var propertyInfo = mx.Member as PropertyInfo; 
					498             if (propertyInfo == null) 
						499             { 
						500                 throw new ArgumentException("The lambda expression 'property' should point to a valid Property"); 
						501             } 
					502 

					503             var propName = propertyInfo.Name; 
					504 

					505             var map = GetMapping<T>(); 
					506             var colName = map.FindColumnWithPropertyName(propName).Name; 
					507 

					508             CreateIndex(map.TableName, colName, unique); 
					509         } 
				510 

				511 		public class ColumnInfo 
				512 		{ 
					513 //			public int cid { get; set; } 
					514 

					515 			[Column ("name")] 
					516 			public string Name { get; set; } 
					517 

					518 //			[Column ("type")] 
					519 //			public string ColumnType { get; set; } 
					520 

					521 			public int notnull { get; set; } 
					522 

					523 //			public string dflt_value { get; set; } 
					524 

					525 //			public int pk { get; set; } 
					526 

					527 			public override string ToString () 
					528 			{ 
						529 				return Name; 
						530 			} 
					531 		} 
				532 

				533 		public List<ColumnInfo> GetTableInfo (string tableName) 
				534 		{ 
					535 			var query = "pragma table_info(\"" + tableName + "\")";			 
					536 			return Query<ColumnInfo> (query); 
					537 		} 
				538 

				539 		void MigrateTable (TableMapping map) 
				540 		{ 
					541 			var existingCols = GetTableInfo (map.TableName); 
					542 			 
					543 			var toBeAdded = new List<TableMapping.Column> (); 
					544 			 
					545 			foreach (var p in map.Columns) { 
						546 				var found = false; 
						547 				foreach (var c in existingCols) { 
							548 					found = (string.Compare (p.Name, c.Name, StringComparison.OrdinalIgnoreCase) == 0); 
							549 					if (found) 
								550 						break; 
							551 				} 
						552 				if (!found) { 
							553 					toBeAdded.Add (p); 
							554 				} 
						555 			} 
					556 			 
					557 			foreach (var p in toBeAdded) { 
						558 				var addCol = "alter table \"" + map.TableName + "\" add column " + Orm.SqlDecl (p, StoreDateTimeAsTicks); 
						559 				Execute (addCol); 
						560 			} 
					561 		} 
				562 

				563 		/// <summary> 
				564 		/// Creates a new SQLiteCommand. Can be overridden to provide a sub-class. 
				565 		/// </summary> 
				566 		/// <seealso cref="SQLiteCommand.OnInstanceCreated"/> 
				567 		protected virtual SQLiteCommand NewCommand () 
				568 		{ 
					569 			return new SQLiteCommand (this); 
					570 		} 
				571 

				572 		/// <summary> 
				573 		/// Creates a new SQLiteCommand given the command text with arguments. Place a '?' 
				574 		/// in the command text for each of the arguments. 
				575 		/// </summary> 
				576 		/// <param name="cmdText"> 
				577 		/// The fully escaped SQL. 
				578 		/// </param> 
				579 		/// <param name="args"> 
				580 		/// Arguments to substitute for the occurences of '?' in the command text. 
				581 		/// </param> 
				582 		/// <returns> 
				583 		/// A <see cref="SQLiteCommand"/> 
				584 		/// </returns> 
				585 		public SQLiteCommand CreateCommand (string cmdText, params object[] ps) 
				586 		{ 
					587 			if (!_open) 
						588 				throw SQLiteException.New (SQLite3.Result.Error, "Cannot create commands from unopened database"); 
					589 

					590 			var cmd = NewCommand (); 
					591 			cmd.CommandText = cmdText; 
					592 			foreach (var o in ps) { 
						593 				cmd.Bind (o); 
						594 			} 
					595 			return cmd; 
					596 		} 
				597 

				598 		/// <summary> 
				599 		/// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?' 
				600 		/// in the command text for each of the arguments and then executes that command. 
				601 		/// Use this method instead of Query when you don't expect rows back. Such cases include 
				602 		/// INSERTs, UPDATEs, and DELETEs. 
				603 		/// You can set the Trace or TimeExecution properties of the connection 
				604 		/// to profile execution. 
				605 		/// </summary> 
				606 		/// <param name="query"> 
				607 		/// The fully escaped SQL. 
				608 		/// </param> 
				609 		/// <param name="args"> 
				610 		/// Arguments to substitute for the occurences of '?' in the query. 
				611 		/// </param> 
				612 		/// <returns> 
				613 		/// The number of rows modified in the database as a result of this execution. 
				614 		/// </returns> 
				615 		public int Execute (string query, params object[] args) 
				616 		{ 
					617 			var cmd = CreateCommand (query, args); 
					618 			 
					619 			if (TimeExecution) { 
						620 				if (_sw == null) { 
							621 					_sw = new Stopwatch (); 
							622 				} 
						623 				_sw.Reset (); 
						624 				_sw.Start (); 
						625 			} 
					626 

					627 			var r = cmd.ExecuteNonQuery (); 
					628 			 
					629 			if (TimeExecution) { 
						630 				_sw.Stop (); 
						631 				_elapsedMilliseconds += _sw.ElapsedMilliseconds; 
						632 				Debug.WriteLine (string.Format ("Finished in {0} ms ({1:0.0} s total)", _sw.ElapsedMilliseconds, _elapsedMilliseconds / 1000.0)); 
						633 			} 
					634 			 
					635 			return r; 
					636 		} 
				637 

				638 		public T ExecuteScalar<T> (string query, params object[] args) 
				639 		{ 
					640 			var cmd = CreateCommand (query, args); 
					641 			 
					642 			if (TimeExecution) { 
						643 				if (_sw == null) { 
							644 					_sw = new Stopwatch (); 
							645 				} 
						646 				_sw.Reset (); 
						647 				_sw.Start (); 
						648 			} 
					649 			 
					650 			var r = cmd.ExecuteScalar<T> (); 
					651 			 
					652 			if (TimeExecution) { 
						653 				_sw.Stop (); 
						654 				_elapsedMilliseconds += _sw.ElapsedMilliseconds; 
						655 				Debug.WriteLine (string.Format ("Finished in {0} ms ({1:0.0} s total)", _sw.ElapsedMilliseconds, _elapsedMilliseconds / 1000.0)); 
						656 			} 
					657 			 
					658 			return r; 
					659 		} 
				660 

				661 		/// <summary> 
				662 		/// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?' 
				663 		/// in the command text for each of the arguments and then executes that command. 
				664 		/// It returns each row of the result using the mapping automatically generated for 
				665 		/// the given type. 
				666 		/// </summary> 
				667 		/// <param name="query"> 
				668 		/// The fully escaped SQL. 
				669 		/// </param> 
				670 		/// <param name="args"> 
				671 		/// Arguments to substitute for the occurences of '?' in the query. 
				672 		/// </param> 
				673 		/// <returns> 
				674 		/// An enumerable with one result for each row returned by the query. 
				675 		/// </returns> 
				676 		public List<T> Query<T> (string query, params object[] args) where T : new() 
				677 		{ 
					678 			var cmd = CreateCommand (query, args); 
					679 			return cmd.ExecuteQuery<T> (); 
					680 		} 
				681 

				682 		/// <summary> 
				683 		/// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?' 
				684 		/// in the command text for each of the arguments and then executes that command. 
				685 		/// It returns each row of the result using the mapping automatically generated for 
				686 		/// the given type. 
				687 		/// </summary> 
				688 		/// <param name="query"> 
				689 		/// The fully escaped SQL. 
				690 		/// </param> 
				691 		/// <param name="args"> 
				692 		/// Arguments to substitute for the occurences of '?' in the query. 
				693 		/// </param> 
				694 		/// <returns> 
				695 		/// An enumerable with one result for each row returned by the query. 
				696 		/// The enumerator will call sqlite3_step on each call to MoveNext, so the database 
				697 		/// connection must remain open for the lifetime of the enumerator. 
				698 		/// </returns> 
				699 		public IEnumerable<T> DeferredQuery<T>(string query, params object[] args) where T : new() 
				700 		{ 
					701 			var cmd = CreateCommand(query, args); 
					702 			return cmd.ExecuteDeferredQuery<T>(); 
					703 		} 
				704 

				705 		/// <summary> 
				706 		/// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?' 
				707 		/// in the command text for each of the arguments and then executes that command. 
				708 		/// It returns each row of the result using the specified mapping. This function is 
				709 		/// only used by libraries in order to query the database via introspection. It is 
				710 		/// normally not used. 
				711 		/// </summary> 
				712 		/// <param name="map"> 
				713 		/// A <see cref="TableMapping"/> to use to convert the resulting rows 
				714 		/// into objects. 
				715 		/// </param> 
				716 		/// <param name="query"> 
				717 		/// The fully escaped SQL. 
				718 		/// </param> 
				719 		/// <param name="args"> 
				720 		/// Arguments to substitute for the occurences of '?' in the query. 
				721 		/// </param> 
				722 		/// <returns> 
				723 		/// An enumerable with one result for each row returned by the query. 
				724 		/// </returns> 
				725 		public List<object> Query (TableMapping map, string query, params object[] args) 
				726 		{ 
					727 			var cmd = CreateCommand (query, args); 
					728 			return cmd.ExecuteQuery<object> (map); 
					729 		} 
				730 

				731 		/// <summary> 
				732 		/// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?' 
				733 		/// in the command text for each of the arguments and then executes that command. 
				734 		/// It returns each row of the result using the specified mapping. This function is 
				735 		/// only used by libraries in order to query the database via introspection. It is 
				736 		/// normally not used. 
				737 		/// </summary> 
				738 		/// <param name="map"> 
				739 		/// A <see cref="TableMapping"/> to use to convert the resulting rows 
				740 		/// into objects. 
				741 		/// </param> 
				742 		/// <param name="query"> 
				743 		/// The fully escaped SQL. 
				744 		/// </param> 
				745 		/// <param name="args"> 
				746 		/// Arguments to substitute for the occurences of '?' in the query. 
				747 		/// </param> 
				748 		/// <returns> 
				749 		/// An enumerable with one result for each row returned by the query. 
				750 		/// The enumerator will call sqlite3_step on each call to MoveNext, so the database 
				751 		/// connection must remain open for the lifetime of the enumerator. 
				752 		/// </returns> 
				753 		public IEnumerable<object> DeferredQuery(TableMapping map, string query, params object[] args) 
				754 		{ 
					755 			var cmd = CreateCommand(query, args); 
					756 			return cmd.ExecuteDeferredQuery<object>(map); 
					757 		} 
				758 

				759 		/// <summary> 
				760 		/// Returns a queryable interface to the table represented by the given type. 
				761 		/// </summary> 
				762 		/// <returns> 
				763 		/// A queryable object that is able to translate Where, OrderBy, and Take 
				764 		/// queries into native SQL. 
				765 		/// </returns> 
				766 		public TableQuery<T> Table<T> () where T : new() 
				767 		{ 
					768 			return new TableQuery<T> (this); 
					769 		} 
				770 

				771 		/// <summary> 
				772 		/// Attempts to retrieve an object with the given primary key from the table 
				773 		/// associated with the specified type. Use of this method requires that 
				774 		/// the given type have a designated PrimaryKey (using the PrimaryKeyAttribute). 
				775 		/// </summary> 
				776 		/// <param name="pk"> 
				777 		/// The primary key. 
				778 		/// </param> 
				779 		/// <returns> 
				780 		/// The object with the given primary key. Throws a not found exception 
				781 		/// if the object is not found. 
				782 		/// </returns> 
				783 		public T Get<T> (object pk) where T : new() 
				784 		{ 
					785 			var map = GetMapping (typeof(T)); 
					786 			return Query<T> (map.GetByPrimaryKeySql, pk).First (); 
					787 		} 
				788 

				789         /// <summary> 
				790         /// Attempts to retrieve the first object that matches the predicate from the table 
				791         /// associated with the specified type.  
				792         /// </summary> 
				793         /// <param name="predicate"> 
				794         /// A predicate for which object to find. 
				795         /// </param> 
				796         /// <returns> 
				797         /// The object that matches the given predicate. Throws a not found exception 
				798         /// if the object is not found. 
				799         /// </returns> 
				800         public T Get<T> (Expression<Func<T, bool>> predicate) where T : new() 
				801         { 
					802             return Table<T> ().Where (predicate).First (); 
					803         } 
				804 

				805 		/// <summary> 
				806 		/// Attempts to retrieve an object with the given primary key from the table 
				807 		/// associated with the specified type. Use of this method requires that 
				808 		/// the given type have a designated PrimaryKey (using the PrimaryKeyAttribute). 
				809 		/// </summary> 
				810 		/// <param name="pk"> 
				811 		/// The primary key. 
				812 		/// </param> 
				813 		/// <returns> 
				814 		/// The object with the given primary key or null 
				815 		/// if the object is not found. 
				816 		/// </returns> 
				817 		public T Find<T> (object pk) where T : new () 
				818 		{ 
					819 			var map = GetMapping (typeof (T)); 
					820 			return Query<T> (map.GetByPrimaryKeySql, pk).FirstOrDefault (); 
					821 		} 
				822 

				823 		/// <summary> 
				824 		/// Attempts to retrieve an object with the given primary key from the table 
				825 		/// associated with the specified type. Use of this method requires that 
				826 		/// the given type have a designated PrimaryKey (using the PrimaryKeyAttribute). 
				827 		/// </summary> 
				828 		/// <param name="pk"> 
				829 		/// The primary key. 
				830 		/// </param> 
				831 		/// <param name="map"> 
				832 		/// The TableMapping used to identify the object type. 
				833 		/// </param> 
				834 		/// <returns> 
				835 		/// The object with the given primary key or null 
				836 		/// if the object is not found. 
				837 		/// </returns> 
				838 		public object Find (object pk, TableMapping map) 
				839 		{ 
					840 			return Query (map, map.GetByPrimaryKeySql, pk).FirstOrDefault (); 
					841 		} 
				842 		 
				843 		/// <summary> 
				844         /// Attempts to retrieve the first object that matches the predicate from the table 
				845         /// associated with the specified type.  
				846         /// </summary> 
				847         /// <param name="predicate"> 
				848         /// A predicate for which object to find. 
				849         /// </param> 
				850         /// <returns> 
				851         /// The object that matches the given predicate or null 
				852         /// if the object is not found. 
				853         /// </returns> 
				854         public T Find<T> (Expression<Func<T, bool>> predicate) where T : new() 
				855         { 
					856             return Table<T> ().Where (predicate).FirstOrDefault (); 
					857         } 
				858 

				859 		/// <summary> 
				860 		/// Attempts to retrieve the first object that matches the query from the table 
				861 		/// associated with the specified type.  
				862 		/// </summary> 
				863 		/// <param name="query"> 
				864 		/// The fully escaped SQL. 
				865 		/// </param> 
				866 		/// <param name="args"> 
				867 		/// Arguments to substitute for the occurences of '?' in the query. 
				868 		/// </param> 
				869 		/// <returns> 
				870 		/// The object that matches the given predicate or null 
				871 		/// if the object is not found. 
				872 		/// </returns> 
				873 		public T FindWithQuery<T> (string query, params object[] args) where T : new() 
				874 		{ 
					875 			return Query<T> (query, args).FirstOrDefault (); 
					876 		} 
				877 

				878 		/// <summary> 
				879 		/// Whether <see cref="BeginTransaction"/> has been called and the database is waiting for a <see cref="Commit"/>. 
				880 		/// </summary> 
				881 		public bool IsInTransaction { 
					882 			get { return _transactionDepth > 0; } 
					883 		} 
				884 

				885 		/// <summary> 
				886 		/// Begins a new transaction. Call <see cref="Commit"/> to end the transaction. 
				887 		/// </summary> 
				888 		/// <example cref="System.InvalidOperationException">Throws if a transaction has already begun.</example> 
				889 		public void BeginTransaction () 
				890 		{ 
					891 			// The BEGIN command only works if the transaction stack is empty,  
					892 			//    or in other words if there are no pending transactions.  
					893 			// If the transaction stack is not empty when the BEGIN command is invoked,  
					894 			//    then the command fails with an error. 
					895 			// Rather than crash with an error, we will just ignore calls to BeginTransaction 
					896 			//    that would result in an error. 
					897 			if (Interlocked.CompareExchange (ref _transactionDepth, 1, 0) == 0) { 
						898 				try { 
							899 					Execute ("begin transaction"); 
							900 				} catch (Exception ex) { 
							901 					var sqlExp = ex as SQLiteException; 
							902 					if (sqlExp != null) { 
								903 						// It is recommended that applications respond to the errors listed below  
								904 						//    by explicitly issuing a ROLLBACK command. 
								905 						// TODO: This rollback failsafe should be localized to all throw sites. 
								906 						switch (sqlExp.Result) { 
								907 						case SQLite3.Result.IOError: 
								908 						case SQLite3.Result.Full: 
								909 						case SQLite3.Result.Busy: 
								910 						case SQLite3.Result.NoMem: 
								911 						case SQLite3.Result.Interrupt: 
									912 							RollbackTo (null, true); 
									913 							break; 
								914 						} 
							915 					} else { 
								916 						// Call decrement and not VolatileWrite in case we've already  
								917 						//    created a transaction point in SaveTransactionPoint since the catch. 
								918 						Interlocked.Decrement (ref _transactionDepth); 
								919 					} 
							920 

							921 					throw; 
							922 				} 
					923 			} else {  
						924 				// Calling BeginTransaction on an already open transaction is invalid 
						925 				throw new InvalidOperationException ("Cannot begin a transaction while already in a transaction."); 
						926 			} 
					927 		} 
				928 

				929 		/// <summary> 
				930 		/// Creates a savepoint in the database at the current point in the transaction timeline. 
				931 		/// Begins a new transaction if one is not in progress. 
				932 		///  
				933 		/// Call <see cref="RollbackTo"/> to undo transactions since the returned savepoint. 
				934 		/// Call <see cref="Release"/> to commit transactions after the savepoint returned here. 
				935 		/// Call <see cref="Commit"/> to end the transaction, committing all changes. 
				936 		/// </summary> 
				937 		/// <returns>A string naming the savepoint.</returns> 
				938 		public string SaveTransactionPoint () 
				939 		{ 
					940 			int depth = Interlocked.Increment (ref _transactionDepth) - 1; 
					941 			string retVal = "S" + _rand.Next (short.MaxValue) + "D" + depth; 
					942 

					943 			try { 
						944 				Execute ("savepoint " + retVal); 
						945 			} catch (Exception ex) { 
						946 				var sqlExp = ex as SQLiteException; 
						947 				if (sqlExp != null) { 
							948 					// It is recommended that applications respond to the errors listed below  
							949 					//    by explicitly issuing a ROLLBACK command. 
							950 					// TODO: This rollback failsafe should be localized to all throw sites. 
							951 					switch (sqlExp.Result) { 
							952 					case SQLite3.Result.IOError: 
							953 					case SQLite3.Result.Full: 
							954 					case SQLite3.Result.Busy: 
							955 					case SQLite3.Result.NoMem: 
							956 					case SQLite3.Result.Interrupt: 
								957 						RollbackTo (null, true); 
								958 						break; 
							959 					} 
						960 				} else { 
							961 					Interlocked.Decrement (ref _transactionDepth); 
							962 				} 
						963 

						964 				throw; 
						965 			} 
					966 

					967 			return retVal; 
					968 		} 
				969 

				970 		/// <summary> 
				971 		/// Rolls back the transaction that was begun by <see cref="BeginTransaction"/> or <see cref="SaveTransactionPoint"/>. 
				972 		/// </summary> 
				973 		public void Rollback () 
				974 		{ 
					975 			RollbackTo (null, false); 
					976 		} 
				977 

				978 		/// <summary> 
				979 		/// Rolls back the savepoint created by <see cref="BeginTransaction"/> or SaveTransactionPoint. 
				980 		/// </summary> 
				981 		/// <param name="savepoint">The name of the savepoint to roll back to, as returned by <see cref="SaveTransactionPoint"/>.  If savepoint is null or empty, this method is equivalent to a call to <see cref="Rollback"/></param> 
				982 		public void RollbackTo (string savepoint) 
				983 		{ 
					984 			RollbackTo (savepoint, false); 
					985 		} 
				986 

				987 		/// <summary> 
				988 		/// Rolls back the transaction that was begun by <see cref="BeginTransaction"/>. 
				989 		/// </summary> 
				990 		/// <param name="noThrow">true to avoid throwing exceptions, false otherwise</param> 
				991 		void RollbackTo (string savepoint, bool noThrow) 
				992 		{ 
					993 			// Rolling back without a TO clause rolls backs all transactions  
					994 			//    and leaves the transaction stack empty.    
					995 			try { 
						996 				if (String.IsNullOrEmpty (savepoint)) { 
							997 					if (Interlocked.Exchange (ref _transactionDepth, 0) > 0) { 
								998 						Execute ("rollback"); 
								999 					} 
						1000 				} else { 
							1001 					DoSavePointExecute (savepoint, "rollback to "); 
							1002 				}    
						1003 			} catch (SQLiteException) { 
						1004 				if (!noThrow) 
							1005 					throw; 
						1006              
						1007 			} 
					1008 			// No need to rollback if there are no transactions open. 
					1009 		} 
				1010 

				1011 		/// <summary> 
				1012 		/// Releases a savepoint returned from <see cref="SaveTransactionPoint"/>.  Releasing a savepoint  
				1013 		///    makes changes since that savepoint permanent if the savepoint began the transaction, 
				1014 		///    or otherwise the changes are permanent pending a call to <see cref="Commit"/>. 
				1015 		///  
				1016 		/// The RELEASE command is like a COMMIT for a SAVEPOINT. 
				1017 		/// </summary> 
				1018 		/// <param name="savepoint">The name of the savepoint to release.  The string should be the result of a call to <see cref="SaveTransactionPoint"/></param> 
				1019 		public void Release (string savepoint) 
				1020 		{ 
					1021 			DoSavePointExecute (savepoint, "release "); 
					1022 		} 
				1023 

				1024 		void DoSavePointExecute (string savepoint, string cmd) 
				1025 		{ 
					1026 			// Validate the savepoint 
					1027 			int firstLen = savepoint.IndexOf ('D'); 
					1028 			if (firstLen >= 2 && savepoint.Length > firstLen + 1) { 
						1029 				int depth; 
						1030 				if (Int32.TryParse (savepoint.Substring (firstLen + 1), out depth)) { 
							1031 					// TODO: Mild race here, but inescapable without locking almost everywhere. 
							1032 					if (0 <= depth && depth < _transactionDepth) { 
								1033 #if NETFX_CORE || USE_SQLITEPCL_RAW 
									1034                         Volatile.Write (ref _transactionDepth, depth); 
								1035 #elif SILVERLIGHT 
								1036 						_transactionDepth = depth; 
								1037 #else 
									1038                         Thread.VolatileWrite (ref _transactionDepth, depth); 
								1039 #endif 
								1040                         Execute (cmd + savepoint); 
								1041 						return; 
								1042 					} 
							1043 				} 
						1044 			} 
					1045 

					1046 			throw new ArgumentException ("savePoint is not valid, and should be the result of a call to SaveTransactionPoint.", "savePoint"); 
					1047 		} 
				1048 

				1049 		/// <summary> 
				1050 		/// Commits the transaction that was begun by <see cref="BeginTransaction"/>. 
				1051 		/// </summary> 
				1052 		public void Commit () 
				1053 		{ 
					1054 			if (Interlocked.Exchange (ref _transactionDepth, 0) != 0) { 
						1055 				Execute ("commit"); 
						1056 			} 
					1057 			// Do nothing on a commit with no open transaction 
					1058 		} 
				1059 

				1060 		/// <summary> 
				1061 		/// Executes <param name="action"> within a (possibly nested) transaction by wrapping it in a SAVEPOINT. If an 
				1062 		/// exception occurs the whole transaction is rolled back, not just the current savepoint. The exception 
				1063 		/// is rethrown. 
				1064 		/// </summary> 
				1065 		/// <param name="action"> 
				1066 		/// The <see cref="Action"/> to perform within a transaction. <param name="action"> can contain any number 
				1067 		/// of operations on the connection but should never call <see cref="BeginTransaction"/> or 
				1068 		/// <see cref="Commit"/>. 
				1069 		/// </param> 
				1070 		public void RunInTransaction (Action action) 
				1071 		{ 
					1072 			try { 
						1073 				var savePoint = SaveTransactionPoint (); 
						1074 				action (); 
						1075 				Release (savePoint); 
						1076 			} catch (Exception) { 
						1077 				Rollback (); 
						1078 				throw; 
						1079 			} 
					1080 		} 
				1081 

				1082 		/// <summary> 
				1083 		/// Inserts all specified objects. 
				1084 		/// </summary> 
				1085 		/// <param name="objects"> 
				1086 		/// An <see cref="IEnumerable"/> of the objects to insert. 
				1087 		/// </param> 
				1088 		/// <returns> 
				1089 		/// The number of rows added to the table. 
				1090 		/// </returns> 
				1091 		public int InsertAll (System.Collections.IEnumerable objects) 
				1092 		{ 
					1093 			var c = 0; 
					1094 			RunInTransaction(() => { 
						1095 				foreach (var r in objects) { 
							1096 					c += Insert (r); 
							1097 				} 
						1098 			}); 
					1099 			return c; 
					1100 		} 
				1101 

				1102 		/// <summary> 
				1103 		/// Inserts all specified objects. 
				1104 		/// </summary> 
				1105 		/// <param name="objects"> 
				1106 		/// An <see cref="IEnumerable"/> of the objects to insert. 
				1107 		/// </param> 
				1108 		/// <param name="extra"> 
				1109 		/// Literal SQL code that gets placed into the command. INSERT {extra} INTO ... 
				1110 		/// </param> 
				1111 		/// <returns> 
				1112 		/// The number of rows added to the table. 
				1113 		/// </returns> 
				1114 		public int InsertAll (System.Collections.IEnumerable objects, string extra) 
				1115 		{ 
					1116 			var c = 0; 
					1117 			RunInTransaction (() => { 
						1118 				foreach (var r in objects) { 
							1119 					c += Insert (r, extra); 
							1120 				} 
						1121 			}); 
					1122 			return c; 
					1123 		} 
				1124 

				1125 		/// <summary> 
				1126 		/// Inserts all specified objects. 
				1127 		/// </summary> 
				1128 		/// <param name="objects"> 
				1129 		/// An <see cref="IEnumerable"/> of the objects to insert. 
				1130 		/// </param> 
				1131 		/// <param name="objType"> 
				1132 		/// The type of object to insert. 
				1133 		/// </param> 
				1134 		/// <returns> 
				1135 		/// The number of rows added to the table. 
				1136 		/// </returns> 
				1137 		public int InsertAll (System.Collections.IEnumerable objects, Type objType) 
				1138 		{ 
					1139 			var c = 0; 
					1140 			RunInTransaction (() => { 
						1141 				foreach (var r in objects) { 
							1142 					c += Insert (r, objType); 
							1143 				} 
						1144 			}); 
					1145 			return c; 
					1146 		} 
				1147 		 
				1148 		/// <summary> 
				1149 		/// Inserts the given object and retrieves its 
				1150 		/// auto incremented primary key if it has one. 
				1151 		/// </summary> 
				1152 		/// <param name="obj"> 
				1153 		/// The object to insert. 
				1154 		/// </param> 
				1155 		/// <returns> 
				1156 		/// The number of rows added to the table. 
				1157 		/// </returns> 
				1158 		public int Insert (object obj) 
				1159 		{ 
					1160 			if (obj == null) { 
						1161 				return 0; 
						1162 			} 
					1163 			return Insert (obj, "", obj.GetType ()); 
					1164 		} 
				1165 

				1166 		/// <summary> 
				1167 		/// Inserts the given object and retrieves its 
				1168 		/// auto incremented primary key if it has one. 
				1169 		/// If a UNIQUE constraint violation occurs with 
				1170 		/// some pre-existing object, this function deletes 
				1171 		/// the old object. 
				1172 		/// </summary> 
				1173 		/// <param name="obj"> 
				1174 		/// The object to insert. 
				1175 		/// </param> 
				1176 		/// <returns> 
				1177 		/// The number of rows modified. 
				1178 		/// </returns> 
				1179 		public int InsertOrReplace (object obj) 
				1180 		{ 
					1181 			if (obj == null) { 
						1182 				return 0; 
						1183 			} 
					1184 			return Insert (obj, "OR REPLACE", obj.GetType ()); 
					1185 		} 
				1186 

				1187 		/// <summary> 
				1188 		/// Inserts the given object and retrieves its 
				1189 		/// auto incremented primary key if it has one. 
				1190 		/// </summary> 
				1191 		/// <param name="obj"> 
				1192 		/// The object to insert. 
				1193 		/// </param> 
				1194 		/// <param name="objType"> 
				1195 		/// The type of object to insert. 
				1196 		/// </param> 
				1197 		/// <returns> 
				1198 		/// The number of rows added to the table. 
				1199 		/// </returns> 
				1200 		public int Insert (object obj, Type objType) 
				1201 		{ 
					1202 			return Insert (obj, "", objType); 
					1203 		} 
				1204 

				1205 		/// <summary> 
				1206 		/// Inserts the given object and retrieves its 
				1207 		/// auto incremented primary key if it has one. 
				1208 		/// If a UNIQUE constraint violation occurs with 
				1209 		/// some pre-existing object, this function deletes 
				1210 		/// the old object. 
				1211 		/// </summary> 
				1212 		/// <param name="obj"> 
				1213 		/// The object to insert. 
				1214 		/// </param> 
				1215 		/// <param name="objType"> 
				1216 		/// The type of object to insert. 
				1217 		/// </param> 
				1218 		/// <returns> 
				1219 		/// The number of rows modified. 
				1220 		/// </returns> 
				1221 		public int InsertOrReplace (object obj, Type objType) 
				1222 		{ 
					1223 			return Insert (obj, "OR REPLACE", objType); 
					1224 		} 
				1225 		 
				1226 		/// <summary> 
				1227 		/// Inserts the given object and retrieves its 
				1228 		/// auto incremented primary key if it has one. 
				1229 		/// </summary> 
				1230 		/// <param name="obj"> 
				1231 		/// The object to insert. 
				1232 		/// </param> 
				1233 		/// <param name="extra"> 
				1234 		/// Literal SQL code that gets placed into the command. INSERT {extra} INTO ... 
				1235 		/// </param> 
				1236 		/// <returns> 
				1237 		/// The number of rows added to the table. 
				1238 		/// </returns> 
				1239 		public int Insert (object obj, string extra) 
				1240 		{ 
					1241 			if (obj == null) { 
						1242 				return 0; 
						1243 			} 
					1244 			return Insert (obj, extra, obj.GetType ()); 
					1245 		} 
				1246 

				1247 	    /// <summary> 
				1248 	    /// Inserts the given object and retrieves its 
				1249 	    /// auto incremented primary key if it has one. 
				1250 	    /// </summary> 
				1251 	    /// <param name="obj"> 
				1252 	    /// The object to insert. 
				1253 	    /// </param> 
				1254 	    /// <param name="extra"> 
				1255 	    /// Literal SQL code that gets placed into the command. INSERT {extra} INTO ... 
				1256 	    /// </param> 
				1257 	    /// <param name="objType"> 
				1258 	    /// The type of object to insert. 
				1259 	    /// </param> 
				1260 	    /// <returns> 
				1261 	    /// The number of rows added to the table. 
				1262 	    /// </returns> 
				1263 	    public int Insert (object obj, string extra, Type objType) 
				1264 		{ 
					1265 			if (obj == null || objType == null) { 
						1266 				return 0; 
						1267 			} 
					1268 			 
					1269              
					1270 			var map = GetMapping (objType); 
					1271 

					1272 #if USE_NEW_REFLECTION_API 
					1273             if (map.PK != null && map.PK.IsAutoGuid) 
						1274             { 
						1275                 // no GetProperty so search our way up the inheritance chain till we find it 
						1276                 PropertyInfo prop; 
						1277                 while (objType != null) 
							1278                 { 
							1279                     var info = objType.GetTypeInfo(); 
							1280                     prop = info.GetDeclaredProperty(map.PK.PropertyName); 
							1281                     if (prop != null)  
								1282                     { 
								1283                         if (prop.GetValue(obj, null).Equals(Guid.Empty)) 
									1284                         { 
									1285                             prop.SetValue(obj, Guid.NewGuid(), null); 
									1286                         } 
								1287                         break;  
								1288                     } 
							1289 

							1290                     objType = info.BaseType; 
							1291                 } 
						1292             } 
					1293 #else 
						1294             if (map.PK != null && map.PK.IsAutoGuid) { 
							1295                 var prop = objType.GetProperty(map.PK.PropertyName); 
							1296                 if (prop != null) { 
								1297                     if (prop.GetValue(obj, null).Equals(Guid.Empty)) { 
									1298                         prop.SetValue(obj, Guid.NewGuid(), null); 
									1299                     } 
								1300                 } 
							1301             } 
					1302 #endif 
					1303 

					1304 

					1305 			var replacing = string.Compare (extra, "OR REPLACE", StringComparison.OrdinalIgnoreCase) == 0; 
					1306 			 
					1307 			var cols = replacing ? map.InsertOrReplaceColumns : map.InsertColumns; 
					1308 			var vals = new object[cols.Length]; 
					1309 			for (var i = 0; i < vals.Length; i++) { 
						1310 				vals [i] = cols [i].GetValue (obj); 
						1311 			} 
					1312 			 
					1313 			var insertCmd = map.GetInsertCommand (this, extra); 
					1314 			int count; 
					1315 

					1316 			lock (insertCmd) { 
						1317 				// We lock here to protect the prepared statement returned via GetInsertCommand. 
						1318 				// A SQLite prepared statement can be bound for only one operation at a time. 
						1319 				try { 
							1320 					count = insertCmd.ExecuteNonQuery (vals); 
							1321 				} catch (SQLiteException ex) { 
							1322 					if (SQLite3.ExtendedErrCode (this.Handle) == SQLite3.ExtendedResult.ConstraintNotNull) { 
								1323 						throw NotNullConstraintViolationException.New (ex.Result, ex.Message, map, obj); 
								1324 					} 
							1325 					throw; 
							1326 				} 
						1327 

						1328 				if (map.HasAutoIncPK) { 
							1329 					var id = SQLite3.LastInsertRowid (Handle); 
							1330 					map.SetAutoIncPK (obj, id); 
							1331 				} 
						1332 			} 
					1333 			if (count > 0) 
						1334 				OnTableChanged (map, NotifyTableChangedAction.Insert); 
					1335 

					1336 			return count; 
					1337 		} 
				1338 

				1339 		/// <summary> 
				1340 		/// Updates all of the columns of a table using the specified object 
				1341 		/// except for its primary key. 
				1342 		/// The object is required to have a primary key. 
				1343 		/// </summary> 
				1344 		/// <param name="obj"> 
				1345 		/// The object to update. It must have a primary key designated using the PrimaryKeyAttribute. 
				1346 		/// </param> 
				1347 		/// <returns> 
				1348 		/// The number of rows updated. 
				1349 		/// </returns> 
				1350 		public int Update (object obj) 
				1351 		{ 
					1352 			if (obj == null) { 
						1353 				return 0; 
						1354 			} 
					1355 			return Update (obj, obj.GetType ()); 
					1356 		} 
				1357 

				1358 		/// <summary> 
				1359 		/// Updates all of the columns of a table using the specified object 
				1360 		/// except for its primary key. 
				1361 		/// The object is required to have a primary key. 
				1362 		/// </summary> 
				1363 		/// <param name="obj"> 
				1364 		/// The object to update. It must have a primary key designated using the PrimaryKeyAttribute. 
				1365 		/// </param> 
				1366 		/// <param name="objType"> 
				1367 		/// The type of object to insert. 
				1368 		/// </param> 
				1369 		/// <returns> 
				1370 		/// The number of rows updated. 
				1371 		/// </returns> 
				1372 		public int Update (object obj, Type objType) 
				1373 		{ 
					1374 			int rowsAffected = 0; 
					1375 			if (obj == null || objType == null) { 
						1376 				return 0; 
						1377 			} 
					1378 			 
					1379 			var map = GetMapping (objType); 
					1380 			 
					1381 			var pk = map.PK; 
					1382 			 
					1383 			if (pk == null) { 
						1384 				throw new NotSupportedException ("Cannot update " + map.TableName + ": it has no PK"); 
						1385 			} 
					1386 			 
					1387 			var cols = from p in map.Columns 
							1388 				where p != pk 
						1389 				select p; 
					1390 			var vals = from c in cols 
						1391 				select c.GetValue (obj); 
					1392 			var ps = new List<object> (vals); 
					1393 			ps.Add (pk.GetValue (obj)); 
					1394 			var q = string.Format ("update \"{0}\" set {1} where {2} = ? ", map.TableName, string.Join (",", (from c in cols 
						1395 				select "\"" + c.Name + "\" = ? ").ToArray ()), pk.Name); 
					1396 

					1397 			try { 
						1398 				rowsAffected = Execute (q, ps.ToArray ()); 
						1399 			} 
					1400 			catch (SQLiteException ex) { 
						1401 

						1402 				if (ex.Result == SQLite3.Result.Constraint && SQLite3.ExtendedErrCode (this.Handle) == SQLite3.ExtendedResult.ConstraintNotNull) { 
							1403 					throw NotNullConstraintViolationException.New (ex, map, obj); 
							1404 				} 
						1405 

						1406 				throw ex; 
						1407 			} 
					1408 

					1409 			if (rowsAffected > 0) 
						1410 				OnTableChanged (map, NotifyTableChangedAction.Update); 
					1411 

					1412 			return rowsAffected; 
					1413 		} 
				1414 

				1415 		/// <summary> 
				1416 		/// Updates all specified objects. 
				1417 		/// </summary> 
				1418 		/// <param name="objects"> 
				1419 		/// An <see cref="IEnumerable"/> of the objects to insert. 
				1420 		/// </param> 
				1421 		/// <returns> 
				1422 		/// The number of rows modified. 
				1423 		/// </returns> 
				1424 		public int UpdateAll (System.Collections.IEnumerable objects) 
				1425 		{ 
					1426 			var c = 0; 
					1427 			RunInTransaction (() => { 
						1428 				foreach (var r in objects) { 
							1429 					c += Update (r); 
							1430 				} 
						1431 			}); 
					1432 			return c; 
					1433 		} 
				1434 

				1435 		/// <summary> 
				1436 		/// Deletes the given object from the database using its primary key. 
				1437 		/// </summary> 
				1438 		/// <param name="objectToDelete"> 
				1439 		/// The object to delete. It must have a primary key designated using the PrimaryKeyAttribute. 
				1440 		/// </param> 
				1441 		/// <returns> 
				1442 		/// The number of rows deleted. 
				1443 		/// </returns> 
				1444 		public int Delete (object objectToDelete) 
				1445 		{ 
					1446 			var map = GetMapping (objectToDelete.GetType ()); 
					1447 			var pk = map.PK; 
					1448 			if (pk == null) { 
						1449 				throw new NotSupportedException ("Cannot delete " + map.TableName + ": it has no PK"); 
						1450 			} 
					1451 			var q = string.Format ("delete from \"{0}\" where \"{1}\" = ?", map.TableName, pk.Name); 
					1452 			var count = Execute (q, pk.GetValue (objectToDelete)); 
					1453 			if (count > 0) 
						1454 				OnTableChanged (map, NotifyTableChangedAction.Delete); 
					1455 			return count; 
					1456 		} 
				1457 

				1458 		/// <summary> 
				1459 		/// Deletes the object with the specified primary key. 
				1460 		/// </summary> 
				1461 		/// <param name="primaryKey"> 
				1462 		/// The primary key of the object to delete. 
				1463 		/// </param> 
				1464 		/// <returns> 
				1465 		/// The number of objects deleted. 
				1466 		/// </returns> 
				1467 		/// <typeparam name='T'> 
				1468 		/// The type of object. 
				1469 		/// </typeparam> 
				1470 		public int Delete<T> (object primaryKey) 
				1471 		{ 
					1472 			var map = GetMapping (typeof (T)); 
					1473 			var pk = map.PK; 
					1474 			if (pk == null) { 
						1475 				throw new NotSupportedException ("Cannot delete " + map.TableName + ": it has no PK"); 
						1476 			} 
					1477 			var q = string.Format ("delete from \"{0}\" where \"{1}\" = ?", map.TableName, pk.Name); 
					1478 			var count = Execute (q, primaryKey); 
					1479 			if (count > 0) 
						1480 				OnTableChanged (map, NotifyTableChangedAction.Delete); 
					1481 			return count; 
					1482 		} 
				1483 

				1484 		/// <summary> 
				1485 		/// Deletes all the objects from the specified table. 
				1486 		/// WARNING WARNING: Let me repeat. It deletes ALL the objects from the 
				1487 		/// specified table. Do you really want to do that? 
				1488 		/// </summary> 
				1489 		/// <returns> 
				1490 		/// The number of objects deleted. 
				1491 		/// </returns> 
				1492 		/// <typeparam name='T'> 
				1493 		/// The type of objects to delete. 
				1494 		/// </typeparam> 
				1495 		public int DeleteAll<T> () 
				1496 		{ 
					1497 			var map = GetMapping (typeof (T)); 
					1498 			var query = string.Format("delete from \"{0}\"", map.TableName); 
					1499 			var count = Execute (query); 
					1500 			if (count > 0) 
						1501 				OnTableChanged (map, NotifyTableChangedAction.Delete); 
					1502 			return count; 
					1503 		} 
				1504 

				1505 		~SQLiteConnection () 
				1506 		{ 
					1507 			Dispose (false); 
					1508 		} 
				1509 

				1510 		public void Dispose () 
				1511 		{ 
					1512 			Dispose (true); 
					1513 			GC.SuppressFinalize (this); 
					1514 		} 
				1515 

				1516 		protected virtual void Dispose (bool disposing) 
				1517 		{ 
					1518 			Close (); 
					1519 		} 
				1520 

				1521 		public void Close () 
				1522 		{ 
					1523 			if (_open && Handle != NullHandle) { 
						1524 				try { 
							1525 					if (_mappings != null) { 
								1526 						foreach (var sqlInsertCommand in _mappings.Values) { 
									1527 							sqlInsertCommand.Dispose(); 
									1528 						} 
								1529 					} 
							1530 					var r = SQLite3.Close (Handle); 
							1531 					if (r != SQLite3.Result.OK) { 
								1532 						string msg = SQLite3.GetErrmsg (Handle); 
								1533 						throw SQLiteException.New (r, msg); 
								1534 					} 
							1535 				} 
						1536 				finally { 
							1537 					Handle = NullHandle; 
							1538 					_open = false; 
							1539 				} 
						1540 			} 
					1541 		} 
				1542 

				1543 		void OnTableChanged (TableMapping table, NotifyTableChangedAction action) 
				1544 		{ 
					1545 			var ev = TableChanged; 
					1546 			if (ev != null) 
						1547 				ev (this, new NotifyTableChangedEventArgs (table, action)); 
					1548 		} 
				1549 

				1550 		public event EventHandler<NotifyTableChangedEventArgs> TableChanged; 
				1551 	} 
			1552 

			1553 	public class NotifyTableChangedEventArgs : EventArgs 
			1554 	{ 
				1555 		public TableMapping Table { get; private set; } 
				1556 		public NotifyTableChangedAction Action { get; private set; } 
				1557 

				1558 		public NotifyTableChangedEventArgs (TableMapping table, NotifyTableChangedAction action) 
				1559 		{ 
					1560 			Table = table; 
					1561 			Action = action;		 
					1562 		} 
				1563 	} 
			1564 

			1565 	public enum NotifyTableChangedAction 
			1566 	{ 
				1567 		Insert, 
				1568 		Update, 
				1569 		Delete, 
				1570 	} 
			1571 

			1572 	/// <summary> 
			1573 	/// Represents a parsed connection string. 
			1574 	/// </summary> 
			1575 	class SQLiteConnectionString 
			1576 	{ 
				1577 		public string ConnectionString { get; private set; } 
				1578 		public string DatabasePath { get; private set; } 
				1579 		public bool StoreDateTimeAsTicks { get; private set; } 
				1580 

				1581 #if NETFX_CORE 
					1582 		static readonly string MetroStyleDataPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path; 
				1583 #endif 
				1584 

				1585 		public SQLiteConnectionString (string databasePath, bool storeDateTimeAsTicks) 
				1586 		{ 
					1587 			ConnectionString = databasePath; 
					1588 			StoreDateTimeAsTicks = storeDateTimeAsTicks; 
					1589 

					1590 #if NETFX_CORE 
						1591 			DatabasePath = System.IO.Path.Combine (MetroStyleDataPath, databasePath); 
					1592 #else 
						1593 			DatabasePath = databasePath; 
					1594 #endif 
					1595 		} 
				1596 	} 
			1597 

			1598     [AttributeUsage (AttributeTargets.Class)] 
			1599 	public class TableAttribute : Attribute 
			1600 	{ 
				1601 		public string Name { get; set; } 
				1602 

				1603 		public TableAttribute (string name) 
				1604 		{ 
					1605 			Name = name; 
					1606 		} 
				1607 	} 
			1608 

			1609 	[AttributeUsage (AttributeTargets.Property)] 
			1610 	public class ColumnAttribute : Attribute 
			1611 	{ 
				1612 		public string Name { get; set; } 
				1613 

				1614 		public ColumnAttribute (string name) 
				1615 		{ 
					1616 			Name = name; 
					1617 		} 
				1618 	} 
			1619 

			1620 	[AttributeUsage (AttributeTargets.Property)] 
			1621 	public class PrimaryKeyAttribute : Attribute 
			1622 	{ 
				1623 	} 
			1624 

			1625 	[AttributeUsage (AttributeTargets.Property)] 
			1626 	public class AutoIncrementAttribute : Attribute 
			1627 	{ 
				1628 	} 
			1629 

			1630 	[AttributeUsage (AttributeTargets.Property)] 
			1631 	public class IndexedAttribute : Attribute 
			1632 	{ 
				1633 		public string Name { get; set; } 
				1634 		public int Order { get; set; } 
				1635 		public virtual bool Unique { get; set; } 
				1636 		 
				1637 		public IndexedAttribute() 
				1638 		{ 
					1639 		} 
				1640 		 
				1641 		public IndexedAttribute(string name, int order) 
				1642 		{ 
					1643 			Name = name; 
					1644 			Order = order; 
					1645 		} 
				1646 	} 
			1647 

			1648 	[AttributeUsage (AttributeTargets.Property)] 
			1649 	public class IgnoreAttribute : Attribute 
			1650 	{ 
				1651 	} 
			1652 

			1653 	[AttributeUsage (AttributeTargets.Property)] 
			1654 	public class UniqueAttribute : IndexedAttribute 
			1655 	{ 
				1656 		public override bool Unique { 
					1657 			get { return true; } 
					1658 			set { /* throw?  */ } 
					1659 		} 
				1660 	} 
			1661 

			1662 	[AttributeUsage (AttributeTargets.Property)] 
			1663 	public class MaxLengthAttribute : Attribute 
			1664 	{ 
				1665 		public int Value { get; private set; } 
				1666 

				1667 		public MaxLengthAttribute (int length) 
				1668 		{ 
					1669 			Value = length; 
					1670 		} 
				1671 	} 
			1672 

			1673 	[AttributeUsage (AttributeTargets.Property)] 
			1674 	public class CollationAttribute: Attribute 
			1675 	{ 
				1676 		public string Value { get; private set; } 
				1677 

				1678 		public CollationAttribute (string collation) 
				1679 		{ 
					1680 			Value = collation; 
					1681 		} 
				1682 	} 
			1683 

			1684 	[AttributeUsage (AttributeTargets.Property)] 
			1685 	public class NotNullAttribute : Attribute 
			1686 	{ 
				1687 	} 
			1688 

			1689 	public class TableMapping 
			1690 	{ 
				1691 		public Type MappedType { get; private set; } 
				1692 

				1693 		public string TableName { get; private set; } 
				1694 

				1695 		public Column[] Columns { get; private set; } 
				1696 

				1697 		public Column PK { get; private set; } 
				1698 

				1699 		public string GetByPrimaryKeySql { get; private set; } 
				1700 

				1701 		Column _autoPk; 
				1702 		Column[] _insertColumns; 
				1703 		Column[] _insertOrReplaceColumns; 
				1704 

				1705         public TableMapping(Type type, CreateFlags createFlags = CreateFlags.None) 
				1706 		{ 
					1707 			MappedType = type; 
					1708 

					1709 #if USE_NEW_REFLECTION_API 
						1710 			var tableAttr = (TableAttribute)System.Reflection.CustomAttributeExtensions 
							1711                 .GetCustomAttribute(type.GetTypeInfo(), typeof(TableAttribute), true); 
					1712 #else 
						1713 			var tableAttr = (TableAttribute)type.GetCustomAttributes (typeof (TableAttribute), true).FirstOrDefault (); 
					1714 #endif 
					1715 

					1716 			TableName = tableAttr != null ? tableAttr.Name : MappedType.Name; 
					1717 

					1718 #if !USE_NEW_REFLECTION_API 
						1719 			var props = MappedType.GetProperties (BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty); 
					1720 #else 
						1721 			var props = from p in MappedType.GetRuntimeProperties() 
								1722 						where ((p.GetMethod != null && p.GetMethod.IsPublic) || (p.SetMethod != null && p.SetMethod.IsPublic) || (p.GetMethod != null && p.GetMethod.IsStatic) || (p.SetMethod != null && p.SetMethod.IsStatic)) 
							1723 						select p; 
					1724 #endif 
					1725 			var cols = new List<Column> (); 
					1726 			foreach (var p in props) { 
						1727 #if !USE_NEW_REFLECTION_API 
							1728 				var ignore = p.GetCustomAttributes (typeof(IgnoreAttribute), true).Length > 0; 
						1729 #else 
							1730 				var ignore = p.GetCustomAttributes (typeof(IgnoreAttribute), true).Count() > 0; 
						1731 #endif 
						1732 				if (p.CanWrite && !ignore) { 
							1733 					cols.Add (new Column (p, createFlags)); 
							1734 				} 
						1735 			} 
					1736 			Columns = cols.ToArray (); 
					1737 			foreach (var c in Columns) { 
						1738 				if (c.IsAutoInc && c.IsPK) { 
							1739 					_autoPk = c; 
							1740 				} 
						1741 				if (c.IsPK) { 
							1742 					PK = c; 
							1743 				} 
						1744 			} 
					1745 			 
					1746 			HasAutoIncPK = _autoPk != null; 
					1747 

					1748 			if (PK != null) { 
						1749 				GetByPrimaryKeySql = string.Format ("select * from \"{0}\" where \"{1}\" = ?", TableName, PK.Name); 
						1750 			} 
					1751 			else { 
						1752 				// People should not be calling Get/Find without a PK 
						1753 				GetByPrimaryKeySql = string.Format ("select * from \"{0}\" limit 1", TableName); 
						1754 			} 
					1755 			_insertCommandMap = new ConcurrentDictionary<string, PreparedSqlLiteInsertCommand> (); 
					1756 		} 
				1757 

				1758 		public bool HasAutoIncPK { get; private set; } 
				1759 

				1760 		public void SetAutoIncPK (object obj, long id) 
				1761 		{ 
					1762 			if (_autoPk != null) { 
						1763 				_autoPk.SetValue (obj, Convert.ChangeType (id, _autoPk.ColumnType, null)); 
						1764 			} 
					1765 		} 
				1766 

				1767 		public Column[] InsertColumns { 
					1768 			get { 
						1769 				if (_insertColumns == null) { 
							1770 					_insertColumns = Columns.Where (c => !c.IsAutoInc).ToArray (); 
							1771 				} 
						1772 				return _insertColumns; 
						1773 			} 
					1774 		} 
				1775 

				1776 		public Column[] InsertOrReplaceColumns { 
					1777 			get { 
						1778 				if (_insertOrReplaceColumns == null) { 
							1779 					_insertOrReplaceColumns = Columns.ToArray (); 
							1780 				} 
						1781 				return _insertOrReplaceColumns; 
						1782 			} 
					1783 		} 
				1784 

				1785 		public Column FindColumnWithPropertyName (string propertyName) 
				1786 		{ 
					1787 			var exact = Columns.FirstOrDefault (c => c.PropertyName == propertyName); 
					1788 			return exact; 
					1789 		} 
				1790 

				1791 		public Column FindColumn (string columnName) 
				1792 		{ 
					1793 			var exact = Columns.FirstOrDefault (c => c.Name == columnName); 
					1794 			return exact; 
					1795 		} 
				1796 		 
				1797 		ConcurrentDictionary<string, PreparedSqlLiteInsertCommand> _insertCommandMap; 
				1798 

				1799 		public PreparedSqlLiteInsertCommand GetInsertCommand(SQLiteConnection conn, string extra) 
				1800 		{ 
					1801 			PreparedSqlLiteInsertCommand prepCmd; 
					1802 			if (!_insertCommandMap.TryGetValue (extra, out prepCmd)) { 
						1803 				prepCmd = CreateInsertCommand (conn, extra); 
						1804 				if (!_insertCommandMap.TryAdd (extra, prepCmd)) { 
							1805 					// Concurrent add attempt beat us. 
							1806 					prepCmd.Dispose (); 
							1807 					_insertCommandMap.TryGetValue (extra, out prepCmd); 
							1808 				} 
						1809 			} 
					1810 			return prepCmd; 
					1811 		} 
				1812 		 
				1813 		PreparedSqlLiteInsertCommand CreateInsertCommand(SQLiteConnection conn, string extra) 
				1814 		{ 
					1815 			var cols = InsertColumns; 
					1816 		    string insertSql; 
					1817             if (!cols.Any() && Columns.Count() == 1 && Columns[0].IsAutoInc) 
						1818             { 
						1819                 insertSql = string.Format("insert {1} into \"{0}\" default values", TableName, extra); 
						1820             } 
					1821             else 
						1822             { 
						1823 				var replacing = string.Compare (extra, "OR REPLACE", StringComparison.OrdinalIgnoreCase) == 0; 
						1824 

						1825 				if (replacing) { 
							1826 					cols = InsertOrReplaceColumns; 
							1827 				} 
						1828 

						1829                 insertSql = string.Format("insert {3} into \"{0}\"({1}) values ({2})", TableName, 
							1830                                    string.Join(",", (from c in cols 
								1831                                                      select "\"" + c.Name + "\"").ToArray()), 
							1832                                    string.Join(",", (from c in cols 
								1833                                                      select "?").ToArray()), extra); 
						1834                  
						1835             } 
					1836 			 
					1837 			var insertCommand = new PreparedSqlLiteInsertCommand(conn); 
					1838 			insertCommand.CommandText = insertSql; 
					1839 			return insertCommand; 
					1840 		} 
				1841 		 
				1842 		protected internal void Dispose() 
				1843 		{ 
					1844 			foreach (var pair in _insertCommandMap) { 
						1845 				pair.Value.Dispose (); 
						1846 			} 
					1847 			_insertCommandMap = null; 
					1848 		} 
				1849 

				1850 		public class Column 
				1851 		{ 
					1852 			PropertyInfo _prop; 
					1853 

					1854 			public string Name { get; private set; } 
					1855 

					1856 			public string PropertyName { get { return _prop.Name; } } 
					1857 

					1858 			public Type ColumnType { get; private set; } 
					1859 

					1860 			public string Collation { get; private set; } 
					1861 

					1862             public bool IsAutoInc { get; private set; } 
					1863             public bool IsAutoGuid { get; private set; } 
					1864 

					1865 			public bool IsPK { get; private set; } 
					1866 

					1867 			public IEnumerable<IndexedAttribute> Indices { get; set; } 
					1868 

					1869 			public bool IsNullable { get; private set; } 
					1870 

					1871 			public int? MaxStringLength { get; private set; } 
					1872 

					1873             public Column(PropertyInfo prop, CreateFlags createFlags = CreateFlags.None) 
					1874             { 
						1875                 var colAttr = (ColumnAttribute)prop.GetCustomAttributes(typeof(ColumnAttribute), true).FirstOrDefault(); 
						1876 

						1877                 _prop = prop; 
						1878                 Name = colAttr == null ? prop.Name : colAttr.Name; 
						1879                 //If this type is Nullable<T> then Nullable.GetUnderlyingType returns the T, otherwise it returns null, so get the actual type instead 
						1880                 ColumnType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType; 
						1881                 Collation = Orm.Collation(prop); 
						1882 

						1883                 IsPK = Orm.IsPK(prop) || 
							1884 					(((createFlags & CreateFlags.ImplicitPK) == CreateFlags.ImplicitPK) && 
								1885 					 	string.Compare (prop.Name, Orm.ImplicitPkName, StringComparison.OrdinalIgnoreCase) == 0); 
						1886 

						1887                 var isAuto = Orm.IsAutoInc(prop) || (IsPK && ((createFlags & CreateFlags.AutoIncPK) == CreateFlags.AutoIncPK)); 
						1888                 IsAutoGuid = isAuto && ColumnType == typeof(Guid); 
						1889                 IsAutoInc = isAuto && !IsAutoGuid; 
						1890 

						1891                 Indices = Orm.GetIndices(prop); 
						1892                 if (!Indices.Any() 
							1893                     && !IsPK 
							1894                     && ((createFlags & CreateFlags.ImplicitIndex) == CreateFlags.ImplicitIndex) 
							1895                     && Name.EndsWith (Orm.ImplicitIndexSuffix, StringComparison.OrdinalIgnoreCase) 
							1896                     ) 
							1897                 { 
							1898                     Indices = new IndexedAttribute[] { new IndexedAttribute() }; 
							1899                 } 
						1900                 IsNullable = !(IsPK || Orm.IsMarkedNotNull(prop)); 
						1901                 MaxStringLength = Orm.MaxStringLength(prop); 
						1902             } 
					1903 

					1904 			public void SetValue (object obj, object val) 
					1905 			{ 
						1906 				_prop.SetValue (obj, val, null); 
						1907 			} 
					1908 

					1909 			public object GetValue (object obj) 
					1910 			{ 
						1911 				return _prop.GetValue (obj, null); 
						1912 			} 
					1913 		} 
				1914 	} 
			1915 

			1916 	public static class Orm 
			1917 	{ 
				1918         public const int DefaultMaxStringLength = 140; 
				1919         public const string ImplicitPkName = "Id"; 
				1920         public const string ImplicitIndexSuffix = "Id"; 
				1921 

				1922 		public static string SqlDecl (TableMapping.Column p, bool storeDateTimeAsTicks) 
				1923 		{ 
					1924 			string decl = "\"" + p.Name + "\" " + SqlType (p, storeDateTimeAsTicks) + " "; 
					1925 			 
					1926 			if (p.IsPK) { 
						1927 				decl += "primary key "; 
						1928 			} 
					1929 			if (p.IsAutoInc) { 
						1930 				decl += "autoincrement "; 
						1931 			} 
					1932 			if (!p.IsNullable) { 
						1933 				decl += "not null "; 
						1934 			} 
					1935 			if (!string.IsNullOrEmpty (p.Collation)) { 
						1936 				decl += "collate " + p.Collation + " "; 
						1937 			} 
					1938 			 
					1939 			return decl; 
					1940 		} 
				1941 

				1942 		public static string SqlType (TableMapping.Column p, bool storeDateTimeAsTicks) 
				1943 		{ 
					1944 			var clrType = p.ColumnType; 
					1945 			if (clrType == typeof(Boolean) || clrType == typeof(Byte) || clrType == typeof(UInt16) || clrType == typeof(SByte) || clrType == typeof(Int16) || clrType == typeof(Int32)) { 
						1946 				return "integer"; 
					1947 			} else if (clrType == typeof(UInt32) || clrType == typeof(Int64)) { 
						1948 				return "bigint"; 
					1949 			} else if (clrType == typeof(Single) || clrType == typeof(Double) || clrType == typeof(Decimal)) { 
						1950 				return "float"; 
					1951 			} else if (clrType == typeof(String)) { 
						1952 				int? len = p.MaxStringLength; 
						1953 

						1954 				if (len.HasValue) 
							1955 					return "varchar(" + len.Value + ")"; 
						1956 

						1957 				return "varchar"; 
					1958 			} else if (clrType == typeof(TimeSpan)) { 
						1959                 return "bigint"; 
					1960 			} else if (clrType == typeof(DateTime)) { 
						1961 				return storeDateTimeAsTicks ? "bigint" : "datetime"; 
					1962 			} else if (clrType == typeof(DateTimeOffset)) { 
						1963 				return "bigint"; 
						1964 #if !USE_NEW_REFLECTION_API 
					1965 			} else if (clrType.IsEnum) { 
						1966 #else 
					1967 			} else if (clrType.GetTypeInfo().IsEnum) { 
						1968 #endif 
						1969 				return "integer"; 
					1970 			} else if (clrType == typeof(byte[])) { 
						1971 				return "blob"; 
					1972             } else if (clrType == typeof(Guid)) { 
						1973                 return "varchar(36)"; 
					1974             } else { 
						1975 				throw new NotSupportedException ("Don't know about " + clrType); 
						1976 			} 
					1977 		} 
				1978 

				1979 		public static bool IsPK (MemberInfo p) 
				1980 		{ 
					1981 			var attrs = p.GetCustomAttributes (typeof(PrimaryKeyAttribute), true); 
					1982 #if !USE_NEW_REFLECTION_API 
						1983 			return attrs.Length > 0; 
					1984 #else 
						1985 			return attrs.Count() > 0; 
					1986 #endif 
					1987 		} 
				1988 

				1989 		public static string Collation (MemberInfo p) 
				1990 		{ 
					1991 			var attrs = p.GetCustomAttributes (typeof(CollationAttribute), true); 
					1992 #if !USE_NEW_REFLECTION_API 
					1993 			if (attrs.Length > 0) { 
						1994 				return ((CollationAttribute)attrs [0]).Value; 
						1995 #else 
							1996 			if (attrs.Count() > 0) { 
								1997                 return ((CollationAttribute)attrs.First()).Value; 
								1998 #endif 
							1999 			} else { 
								2000 				return string.Empty; 
								2001 			} 
						2002 		} 
					2003 

					2004 		public static bool IsAutoInc (MemberInfo p) 
					2005 		{ 
						2006 			var attrs = p.GetCustomAttributes (typeof(AutoIncrementAttribute), true); 
						2007 #if !USE_NEW_REFLECTION_API 
							2008 			return attrs.Length > 0; 
						2009 #else 
							2010 			return attrs.Count() > 0; 
						2011 #endif 
						2012 		} 
					2013 

					2014 		public static IEnumerable<IndexedAttribute> GetIndices(MemberInfo p) 
					2015 		{ 
						2016 			var attrs = p.GetCustomAttributes(typeof(IndexedAttribute), true); 
						2017 			return attrs.Cast<IndexedAttribute>(); 
						2018 		} 
					2019 		 
					2020 		public static int? MaxStringLength(PropertyInfo p) 
					2021 		{ 
						2022 			var attrs = p.GetCustomAttributes (typeof(MaxLengthAttribute), true); 
						2023 #if !USE_NEW_REFLECTION_API 
						2024 			if (attrs.Length > 0) 
							2025 				return ((MaxLengthAttribute)attrs [0]).Value; 
						2026 #else 
							2027 			if (attrs.Count() > 0) 
								2028 				return ((MaxLengthAttribute)attrs.First()).Value; 
						2029 #endif 
						2030 

						2031 			return null; 
						2032 		} 
					2033 

					2034 		public static bool IsMarkedNotNull(MemberInfo p) 
					2035 		{ 
						2036 			var attrs = p.GetCustomAttributes (typeof (NotNullAttribute), true); 
						2037 #if !USE_NEW_REFLECTION_API 
							2038 			return attrs.Length > 0; 
						2039 #else 
							2040 	return attrs.Count() > 0; 
						2041 #endif 
						2042 		} 
					2043 	} 
				2044 

				2045 	public partial class SQLiteCommand 
				2046 	{ 
					2047 		SQLiteConnection _conn; 
					2048 		private List<Binding> _bindings; 
					2049 

					2050 		public string CommandText { get; set; } 
					2051 

					2052 		internal SQLiteCommand (SQLiteConnection conn) 
					2053 		{ 
						2054 			_conn = conn; 
						2055 			_bindings = new List<Binding> (); 
						2056 			CommandText = ""; 
						2057 		} 
					2058 

					2059 		public int ExecuteNonQuery () 
					2060 		{ 
						2061 			if (_conn.Trace) { 
							2062 				Debug.WriteLine ("Executing: " + this); 
							2063 			} 
						2064 			 
						2065 			var r = SQLite3.Result.OK; 
						2066 			var stmt = Prepare (); 
						2067 			r = SQLite3.Step (stmt); 
						2068 			Finalize (stmt); 
						2069 			if (r == SQLite3.Result.Done) { 
							2070 				int rowsAffected = SQLite3.Changes (_conn.Handle); 
							2071 				return rowsAffected; 
						2072 			} else if (r == SQLite3.Result.Error) { 
							2073 				string msg = SQLite3.GetErrmsg (_conn.Handle); 
							2074 				throw SQLiteException.New (r, msg); 
							2075 			} 
						2076 			else if (r == SQLite3.Result.Constraint) { 
							2077 				if (SQLite3.ExtendedErrCode (_conn.Handle) == SQLite3.ExtendedResult.ConstraintNotNull) { 
								2078 					throw NotNullConstraintViolationException.New (r, SQLite3.GetErrmsg (_conn.Handle)); 
								2079 				} 
							2080 			} 
						2081 

						2082 			throw SQLiteException.New(r, r.ToString()); 
						2083 		} 
					2084 

					2085 		public IEnumerable<T> ExecuteDeferredQuery<T> () 
					2086 		{ 
						2087 			return ExecuteDeferredQuery<T>(_conn.GetMapping(typeof(T))); 
						2088 		} 
					2089 

					2090 		public List<T> ExecuteQuery<T> () 
					2091 		{ 
						2092 			return ExecuteDeferredQuery<T>(_conn.GetMapping(typeof(T))).ToList(); 
						2093 		} 
					2094 

					2095 		public List<T> ExecuteQuery<T> (TableMapping map) 
					2096 		{ 
						2097 			return ExecuteDeferredQuery<T>(map).ToList(); 
						2098 		} 
					2099 

					2100 		/// <summary> 
					2101 		/// Invoked every time an instance is loaded from the database. 
					2102 		/// </summary> 
					2103 		/// <param name='obj'> 
					2104 		/// The newly created object. 
					2105 		/// </param> 
					2106 		/// <remarks> 
					2107 		/// This can be overridden in combination with the <see cref="SQLiteConnection.NewCommand"/> 
					2108 		/// method to hook into the life-cycle of objects. 
					2109 		/// 
					2110 		/// Type safety is not possible because MonoTouch does not support virtual generic methods. 
					2111 		/// </remarks> 
					2112 		protected virtual void OnInstanceCreated (object obj) 
					2113 		{ 
						2114 			// Can be overridden. 
						2115 		} 
					2116 

					2117 		public IEnumerable<T> ExecuteDeferredQuery<T> (TableMapping map) 
					2118 		{ 
						2119 			if (_conn.Trace) { 
							2120 				Debug.WriteLine ("Executing Query: " + this); 
							2121 			} 
						2122 

						2123 			var stmt = Prepare (); 
						2124 			try 
						2125 			{ 
							2126 				var cols = new TableMapping.Column[SQLite3.ColumnCount (stmt)]; 
							2127 

							2128 				for (int i = 0; i < cols.Length; i++) { 
								2129 					var name = SQLite3.ColumnName16 (stmt, i); 
								2130 					cols [i] = map.FindColumn (name); 
								2131 				} 
							2132 			 
							2133 				while (SQLite3.Step (stmt) == SQLite3.Result.Row) { 
								2134 					var obj = Activator.CreateInstance(map.MappedType); 
								2135 					for (int i = 0; i < cols.Length; i++) { 
									2136 						if (cols [i] == null) 
										2137 							continue; 
									2138 						var colType = SQLite3.ColumnType (stmt, i); 
									2139 						var val = ReadCol (stmt, i, colType, cols [i].ColumnType); 
									2140 						cols [i].SetValue (obj, val); 
									2141  					} 
								2142 					OnInstanceCreated (obj); 
								2143 					yield return (T)obj; 
								2144 				} 
							2145 			} 
						2146 			finally 
						2147 			{ 
							2148 				SQLite3.Finalize(stmt); 
							2149 			} 
						2150 		} 
					2151 

					2152 		public T ExecuteScalar<T> () 
					2153 		{ 
						2154 			if (_conn.Trace) { 
							2155 				Debug.WriteLine ("Executing Query: " + this); 
							2156 			} 
						2157 			 
						2158 			T val = default(T); 
						2159 			 
						2160 			var stmt = Prepare (); 
						2161 

						2162             try 
						2163             { 
							2164                 var r = SQLite3.Step (stmt); 
							2165                 if (r == SQLite3.Result.Row) { 
								2166                     var colType = SQLite3.ColumnType (stmt, 0); 
								2167                     val = (T)ReadCol (stmt, 0, colType, typeof(T)); 
								2168                 } 
							2169                 else if (r == SQLite3.Result.Done) { 
								2170                 } 
							2171                 else 
								2172                 { 
								2173                     throw SQLiteException.New (r, SQLite3.GetErrmsg (_conn.Handle)); 
								2174                 } 
							2175             } 
						2176             finally 
						2177             { 
							2178                 Finalize (stmt); 
							2179             } 
						2180 			 
						2181 			return val; 
						2182 		} 
					2183 

					2184 		public void Bind (string name, object val) 
					2185 		{ 
						2186 			_bindings.Add (new Binding { 
							2187 				Name = name, 
							2188 				Value = val 
								2189 			}); 
						2190 		} 
					2191 

					2192 		public void Bind (object val) 
					2193 		{ 
						2194 			Bind (null, val); 
						2195 		} 
					2196 

					2197 		public override string ToString () 
					2198 		{ 
						2199 			var parts = new string[1 + _bindings.Count]; 
						2200 			parts [0] = CommandText; 
						2201 			var i = 1; 
						2202 			foreach (var b in _bindings) { 
							2203 				parts [i] = string.Format ("  {0}: {1}", i - 1, b.Value); 
							2204 				i++; 
							2205 			} 
						2206 			return string.Join (Environment.NewLine, parts); 
						2207 		} 
					2208 

					2209 		Sqlite3Statement Prepare() 
					2210 		{ 
						2211 			var stmt = SQLite3.Prepare2 (_conn.Handle, CommandText); 
						2212 			BindAll (stmt); 
						2213 			return stmt; 
						2214 		} 
					2215 

					2216 		void Finalize (Sqlite3Statement stmt) 
					2217 		{ 
						2218 			SQLite3.Finalize (stmt); 
						2219 		} 
					2220 

					2221 		void BindAll (Sqlite3Statement stmt) 
					2222 		{ 
						2223 			int nextIdx = 1; 
						2224 			foreach (var b in _bindings) { 
							2225 				if (b.Name != null) { 
								2226 					b.Index = SQLite3.BindParameterIndex (stmt, b.Name); 
							2227 				} else { 
								2228 					b.Index = nextIdx++; 
								2229 				} 
							2230 				 
							2231 				BindParameter (stmt, b.Index, b.Value, _conn.StoreDateTimeAsTicks); 
							2232 			} 
						2233 		} 
					2234 

					2235 		internal static IntPtr NegativePointer = new IntPtr (-1); 
					2236 

					2237 		internal static void BindParameter (Sqlite3Statement stmt, int index, object value, bool storeDateTimeAsTicks) 
					2238 		{ 
						2239 			if (value == null) { 
							2240 				SQLite3.BindNull (stmt, index); 
						2241 			} else { 
							2242 				if (value is Int32) { 
								2243 					SQLite3.BindInt (stmt, index, (int)value); 
							2244 				} else if (value is String) { 
								2245 					SQLite3.BindText (stmt, index, (string)value, -1, NegativePointer); 
							2246 				} else if (value is Byte || value is UInt16 || value is SByte || value is Int16) { 
								2247 					SQLite3.BindInt (stmt, index, Convert.ToInt32 (value)); 
							2248 				} else if (value is Boolean) { 
								2249 					SQLite3.BindInt (stmt, index, (bool)value ? 1 : 0); 
							2250 				} else if (value is UInt32 || value is Int64) { 
								2251 					SQLite3.BindInt64 (stmt, index, Convert.ToInt64 (value)); 
							2252 				} else if (value is Single || value is Double || value is Decimal) { 
								2253 					SQLite3.BindDouble (stmt, index, Convert.ToDouble (value)); 
							2254 				} else if (value is TimeSpan) { 
								2255 					SQLite3.BindInt64(stmt, index, ((TimeSpan)value).Ticks); 
							2256 				} else if (value is DateTime) { 
								2257 					if (storeDateTimeAsTicks) { 
									2258 						SQLite3.BindInt64 (stmt, index, ((DateTime)value).Ticks); 
									2259 					} 
								2260 					else { 
									2261 						SQLite3.BindText (stmt, index, ((DateTime)value).ToString ("yyyy-MM-dd HH:mm:ss"), -1, NegativePointer); 
									2262 					} 
							2263 				} else if (value is DateTimeOffset) { 
								2264 					SQLite3.BindInt64 (stmt, index, ((DateTimeOffset)value).UtcTicks); 
								2265 #if !USE_NEW_REFLECTION_API 
							2266 				} else if (value.GetType().IsEnum) { 
								2267 #else 
							2268 				} else if (value.GetType().GetTypeInfo().IsEnum) { 
								2269 #endif 
								2270 					SQLite3.BindInt (stmt, index, Convert.ToInt32 (value)); 
							2271                 } else if (value is byte[]){ 
								2272                     SQLite3.BindBlob(stmt, index, (byte[]) value, ((byte[]) value).Length, NegativePointer); 
							2273                 } else if (value is Guid) { 
								2274                     SQLite3.BindText(stmt, index, ((Guid)value).ToString(), 72, NegativePointer); 
							2275                 } else { 
								2276                     throw new NotSupportedException("Cannot store type: " + value.GetType()); 
								2277                 } 
							2278 			} 
						2279 		} 
					2280 

					2281 		class Binding 
					2282 		{ 
						2283 			public string Name { get; set; } 
						2284 

						2285 			public object Value { get; set; } 
						2286 

						2287 			public int Index { get; set; } 
						2288 		} 
					2289 

					2290 		object ReadCol (Sqlite3Statement stmt, int index, SQLite3.ColType type, Type clrType) 
					2291 		{ 
						2292 			if (type == SQLite3.ColType.Null) { 
							2293 				return null; 
						2294 			} else { 
							2295 				if (clrType == typeof(String)) { 
								2296 					return SQLite3.ColumnString (stmt, index); 
							2297 				} else if (clrType == typeof(Int32)) { 
								2298 					return (int)SQLite3.ColumnInt (stmt, index); 
							2299 				} else if (clrType == typeof(Boolean)) { 
								2300 					return SQLite3.ColumnInt (stmt, index) == 1; 
							2301 				} else if (clrType == typeof(double)) { 
								2302 					return SQLite3.ColumnDouble (stmt, index); 
							2303 				} else if (clrType == typeof(float)) { 
								2304 					return (float)SQLite3.ColumnDouble (stmt, index); 
							2305 				} else if (clrType == typeof(TimeSpan)) { 
								2306 					return new TimeSpan(SQLite3.ColumnInt64(stmt, index)); 
							2307 				} else if (clrType == typeof(DateTime)) { 
								2308 					if (_conn.StoreDateTimeAsTicks) { 
									2309 						return new DateTime (SQLite3.ColumnInt64 (stmt, index)); 
									2310 					} 
								2311 					else { 
									2312 						var text = SQLite3.ColumnString (stmt, index); 
									2313 						return DateTime.Parse (text); 
									2314 					} 
							2315 				} else if (clrType == typeof(DateTimeOffset)) { 
								2316 					return new DateTimeOffset(SQLite3.ColumnInt64 (stmt, index),TimeSpan.Zero); 
								2317 #if !USE_NEW_REFLECTION_API 
							2318 				} else if (clrType.IsEnum) { 
								2319 #else 
							2320 				} else if (clrType.GetTypeInfo().IsEnum) { 
								2321 #endif 
								2322 					return SQLite3.ColumnInt (stmt, index); 
							2323 				} else if (clrType == typeof(Int64)) { 
								2324 					return SQLite3.ColumnInt64 (stmt, index); 
							2325 				} else if (clrType == typeof(UInt32)) { 
								2326 					return (uint)SQLite3.ColumnInt64 (stmt, index); 
							2327 				} else if (clrType == typeof(decimal)) { 
								2328 					return (decimal)SQLite3.ColumnDouble (stmt, index); 
							2329 				} else if (clrType == typeof(Byte)) { 
								2330 					return (byte)SQLite3.ColumnInt (stmt, index); 
							2331 				} else if (clrType == typeof(UInt16)) { 
								2332 					return (ushort)SQLite3.ColumnInt (stmt, index); 
							2333 				} else if (clrType == typeof(Int16)) { 
								2334 					return (short)SQLite3.ColumnInt (stmt, index); 
							2335 				} else if (clrType == typeof(sbyte)) { 
								2336 					return (sbyte)SQLite3.ColumnInt (stmt, index); 
							2337 				} else if (clrType == typeof(byte[])) { 
								2338 					return SQLite3.ColumnByteArray (stmt, index); 
							2339 				} else if (clrType == typeof(Guid)) { 
								2340                   var text = SQLite3.ColumnString(stmt, index); 
								2341                   return new Guid(text); 
							2342                 } else{ 
								2343 					throw new NotSupportedException ("Don't know how to read " + clrType); 
								2344 				} 
							2345 			} 
						2346 		} 
					2347 	} 
				2348 

				2349 	/// <summary> 
				2350 	/// Since the insert never changed, we only need to prepare once. 
				2351 	/// </summary> 
				2352 	public class PreparedSqlLiteInsertCommand : IDisposable 
				2353 	{ 
					2354 		public bool Initialized { get; set; } 
					2355 

					2356 		protected SQLiteConnection Connection { get; set; } 
					2357 

					2358 		public string CommandText { get; set; } 
					2359 

					2360 		protected Sqlite3Statement Statement { get; set; } 
					2361 		internal static readonly Sqlite3Statement NullStatement = default(Sqlite3Statement); 
					2362 

					2363 		internal PreparedSqlLiteInsertCommand (SQLiteConnection conn) 
					2364 		{ 
						2365 			Connection = conn; 
						2366 		} 
					2367 

					2368 		public int ExecuteNonQuery (object[] source) 
					2369 		{ 
						2370 			if (Connection.Trace) { 
							2371 				Debug.WriteLine ("Executing: " + CommandText); 
							2372 			} 
						2373 

						2374 			var r = SQLite3.Result.OK; 
						2375 

						2376 			if (!Initialized) { 
							2377 				Statement = Prepare (); 
							2378 				Initialized = true; 
							2379 			} 
						2380 

						2381 			//bind the values. 
						2382 			if (source != null) { 
							2383 				for (int i = 0; i < source.Length; i++) { 
								2384 					SQLiteCommand.BindParameter (Statement, i + 1, source [i], Connection.StoreDateTimeAsTicks); 
								2385 				} 
							2386 			} 
						2387 			r = SQLite3.Step (Statement); 
						2388 

						2389 			if (r == SQLite3.Result.Done) { 
							2390 				int rowsAffected = SQLite3.Changes (Connection.Handle); 
							2391 				SQLite3.Reset (Statement); 
							2392 				return rowsAffected; 
						2393 			} else if (r == SQLite3.Result.Error) { 
							2394 				string msg = SQLite3.GetErrmsg (Connection.Handle); 
							2395 				SQLite3.Reset (Statement); 
							2396 				throw SQLiteException.New (r, msg); 
						2397 			} else if (r == SQLite3.Result.Constraint && SQLite3.ExtendedErrCode (Connection.Handle) == SQLite3.ExtendedResult.ConstraintNotNull) { 
							2398 				SQLite3.Reset (Statement); 
							2399 				throw NotNullConstraintViolationException.New (r, SQLite3.GetErrmsg (Connection.Handle)); 
						2400 			} else { 
							2401 				SQLite3.Reset (Statement); 
							2402 				throw SQLiteException.New (r, r.ToString ()); 
							2403 			} 
						2404 		} 
					2405 

					2406 		protected virtual Sqlite3Statement Prepare () 
					2407 		{ 
						2408 			var stmt = SQLite3.Prepare2 (Connection.Handle, CommandText); 
						2409 			return stmt; 
						2410 		} 
					2411 

					2412 		public void Dispose () 
					2413 		{ 
						2414 			Dispose (true); 
						2415 			GC.SuppressFinalize (this); 
						2416 		} 
					2417 

					2418 		private void Dispose (bool disposing) 
					2419 		{ 
						2420 			if (Statement != NullStatement) { 
							2421 				try { 
								2422 					SQLite3.Finalize (Statement); 
								2423 				} finally { 
								2424 					Statement = NullStatement; 
								2425 					Connection = null; 
								2426 				} 
							2427 			} 
						2428 		} 
					2429 

					2430 		~PreparedSqlLiteInsertCommand () 
					2431 		{ 
						2432 			Dispose (false); 
						2433 		} 
					2434 	} 
				2435 

				2436 	public abstract class BaseTableQuery 
				2437 	{ 
					2438 		protected class Ordering 
					2439 		{ 
						2440 			public string ColumnName { get; set; } 
						2441 			public bool Ascending { get; set; } 
						2442 		} 
					2443 	} 
				2444 

				2445 	public class TableQuery<T> : BaseTableQuery, IEnumerable<T> 
				2446 	{ 
					2447 		public SQLiteConnection Connection { get; private set; } 
					2448 

					2449 		public TableMapping Table { get; private set; } 
					2450 

					2451 		Expression _where; 
					2452 		List<Ordering> _orderBys; 
					2453 		int? _limit; 
					2454 		int? _offset; 
					2455 

					2456 		BaseTableQuery _joinInner; 
					2457 		Expression _joinInnerKeySelector; 
					2458 		BaseTableQuery _joinOuter; 
					2459 		Expression _joinOuterKeySelector; 
					2460 		Expression _joinSelector; 
					2461 				 
					2462 		Expression _selector; 
					2463 

					2464 		TableQuery (SQLiteConnection conn, TableMapping table) 
					2465 		{ 
						2466 			Connection = conn; 
						2467 			Table = table; 
						2468 		} 
					2469 

					2470 		public TableQuery (SQLiteConnection conn) 
					2471 		{ 
						2472 			Connection = conn; 
						2473 			Table = Connection.GetMapping (typeof(T)); 
						2474 		} 
					2475 

					2476 		public TableQuery<U> Clone<U> () 
					2477 		{ 
						2478 			var q = new TableQuery<U> (Connection, Table); 
						2479 			q._where = _where; 
						2480 			q._deferred = _deferred; 
						2481 			if (_orderBys != null) { 
							2482 				q._orderBys = new List<Ordering> (_orderBys); 
							2483 			} 
						2484 			q._limit = _limit; 
						2485 			q._offset = _offset; 
						2486 			q._joinInner = _joinInner; 
						2487 			q._joinInnerKeySelector = _joinInnerKeySelector; 
						2488 			q._joinOuter = _joinOuter; 
						2489 			q._joinOuterKeySelector = _joinOuterKeySelector; 
						2490 			q._joinSelector = _joinSelector; 
						2491 			q._selector = _selector; 
						2492 			return q; 
						2493 		} 
					2494 

					2495 		public TableQuery<T> Where (Expression<Func<T, bool>> predExpr) 
					2496 		{ 
						2497 			if (predExpr.NodeType == ExpressionType.Lambda) { 
							2498 				var lambda = (LambdaExpression)predExpr; 
							2499 				var pred = lambda.Body; 
							2500 				var q = Clone<T> (); 
							2501 				q.AddWhere (pred); 
							2502 				return q; 
						2503 			} else { 
							2504 				throw new NotSupportedException ("Must be a predicate"); 
							2505 			} 
						2506 		} 
					2507 

					2508 		public TableQuery<T> Take (int n) 
					2509 		{ 
						2510 			var q = Clone<T> (); 
						2511 			q._limit = n; 
						2512 			return q; 
						2513 		} 
					2514 

					2515 		public TableQuery<T> Skip (int n) 
					2516 		{ 
						2517 			var q = Clone<T> (); 
						2518 			q._offset = n; 
						2519 			return q; 
						2520 		} 
					2521 

					2522 		public T ElementAt (int index) 
					2523 		{ 
						2524 			return Skip (index).Take (1).First (); 
						2525 		} 
					2526 

					2527 		bool _deferred; 
					2528 		public TableQuery<T> Deferred () 
					2529 		{ 
						2530 			var q = Clone<T> (); 
						2531 			q._deferred = true; 
						2532 			return q; 
						2533 		} 
					2534 

					2535 		public TableQuery<T> OrderBy<U> (Expression<Func<T, U>> orderExpr) 
					2536 		{ 
						2537 			return AddOrderBy<U> (orderExpr, true); 
						2538 		} 
					2539 

					2540 		public TableQuery<T> OrderByDescending<U> (Expression<Func<T, U>> orderExpr) 
					2541 		{ 
						2542 			return AddOrderBy<U> (orderExpr, false); 
						2543 		} 
					2544 

					2545 		public TableQuery<T> ThenBy<U>(Expression<Func<T, U>> orderExpr) 
					2546 		{ 
						2547 			return AddOrderBy<U>(orderExpr, true); 
						2548 		} 
					2549 

					2550 		public TableQuery<T> ThenByDescending<U>(Expression<Func<T, U>> orderExpr) 
					2551 		{ 
						2552 			return AddOrderBy<U>(orderExpr, false); 
						2553 		} 
					2554 

					2555 		private TableQuery<T> AddOrderBy<U> (Expression<Func<T, U>> orderExpr, bool asc) 
					2556 		{ 
						2557 			if (orderExpr.NodeType == ExpressionType.Lambda) { 
							2558 				var lambda = (LambdaExpression)orderExpr; 
							2559 				 
							2560 				MemberExpression mem = null; 
							2561 				 
							2562 				var unary = lambda.Body as UnaryExpression; 
							2563 				if (unary != null && unary.NodeType == ExpressionType.Convert) { 
								2564 					mem = unary.Operand as MemberExpression; 
								2565 				} 
							2566 				else { 
								2567 					mem = lambda.Body as MemberExpression; 
								2568 				} 
							2569 				 
							2570 				if (mem != null && (mem.Expression.NodeType == ExpressionType.Parameter)) { 
								2571 					var q = Clone<T> (); 
								2572 					if (q._orderBys == null) { 
									2573 						q._orderBys = new List<Ordering> (); 
									2574 					} 
								2575 					q._orderBys.Add (new Ordering { 
									2576 						ColumnName = Table.FindColumnWithPropertyName(mem.Member.Name).Name, 
									2577 						Ascending = asc 
										2578 					}); 
								2579 					return q; 
							2580 				} else { 
								2581 					throw new NotSupportedException ("Order By does not support: " + orderExpr); 
								2582 				} 
						2583 			} else { 
							2584 				throw new NotSupportedException ("Must be a predicate"); 
							2585 			} 
						2586 		} 
					2587 

					2588 		private void AddWhere (Expression pred) 
					2589 		{ 
						2590 			if (_where == null) { 
							2591 				_where = pred; 
						2592 			} else { 
							2593 				_where = Expression.AndAlso (_where, pred); 
							2594 			} 
						2595 		} 
					2596 				 
					2597 		public TableQuery<TResult> Join<TInner, TKey, TResult> ( 
						2598 			TableQuery<TInner> inner, 
						2599 			Expression<Func<T, TKey>> outerKeySelector, 
						2600 			Expression<Func<TInner, TKey>> innerKeySelector, 
						2601 			Expression<Func<T, TInner, TResult>> resultSelector) 
					2602 		{ 
						2603 			var q = new TableQuery<TResult> (Connection, Connection.GetMapping (typeof (TResult))) { 
							2604 				_joinOuter = this, 
							2605 				_joinOuterKeySelector = outerKeySelector, 
							2606 				_joinInner = inner, 
							2607 				_joinInnerKeySelector = innerKeySelector, 
							2608 				_joinSelector = resultSelector, 
							2609 			}; 
						2610 			return q; 
						2611 		} 
					2612 				 
					2613 		public TableQuery<TResult> Select<TResult> (Expression<Func<T, TResult>> selector) 
					2614 		{ 
						2615 			var q = Clone<TResult> (); 
						2616 			q._selector = selector; 
						2617 			return q; 
						2618 		} 
					2619 

					2620 		private SQLiteCommand GenerateCommand (string selectionList) 
					2621 		{ 
						2622 			if (_joinInner != null && _joinOuter != null) { 
							2623 				throw new NotSupportedException ("Joins are not supported."); 
							2624 			} 
						2625 			else { 
							2626 				var cmdText = "select " + selectionList + " from \"" + Table.TableName + "\""; 
							2627 				var args = new List<object> (); 
							2628 				if (_where != null) { 
								2629 					var w = CompileExpr (_where, args); 
								2630 					cmdText += " where " + w.CommandText; 
								2631 				} 
							2632 				if ((_orderBys != null) && (_orderBys.Count > 0)) { 
								2633 					var t = string.Join (", ", _orderBys.Select (o => "\"" + o.ColumnName + "\"" + (o.Ascending ? "" : " desc")).ToArray ()); 
								2634 					cmdText += " order by " + t; 
								2635 				} 
							2636 				if (_limit.HasValue) { 
								2637 					cmdText += " limit " + _limit.Value; 
								2638 				} 
							2639 				if (_offset.HasValue) { 
								2640 					if (!_limit.HasValue) { 
									2641 						cmdText += " limit -1 "; 
									2642 					} 
								2643 					cmdText += " offset " + _offset.Value; 
								2644 				} 
							2645 				return Connection.CreateCommand (cmdText, args.ToArray ()); 
							2646 			} 
						2647 		} 
					2648 

					2649 		class CompileResult 
					2650 		{ 
						2651 			public string CommandText { get; set; } 
						2652 

						2653 			public object Value { get; set; } 
						2654 		} 
					2655 

					2656 		private CompileResult CompileExpr (Expression expr, List<object> queryArgs) 
					2657 		{ 
						2658 			if (expr == null) { 
							2659 				throw new NotSupportedException ("Expression is NULL"); 
						2660 			} else if (expr is BinaryExpression) { 
							2661 				var bin = (BinaryExpression)expr; 
							2662 				 
							2663 				var leftr = CompileExpr (bin.Left, queryArgs); 
							2664 				var rightr = CompileExpr (bin.Right, queryArgs); 
							2665 

							2666 				//If either side is a parameter and is null, then handle the other side specially (for "is null"/"is not null") 
							2667 				string text; 
							2668 				if (leftr.CommandText == "?" && leftr.Value == null) 
								2669 					text = CompileNullBinaryExpression(bin, rightr); 
							2670 				else if (rightr.CommandText == "?" && rightr.Value == null) 
								2671 					text = CompileNullBinaryExpression(bin, leftr); 
							2672 				else 
								2673 					text = "(" + leftr.CommandText + " " + GetSqlName(bin) + " " + rightr.CommandText + ")"; 
							2674 				return new CompileResult { CommandText = text }; 
						2675 			} else if (expr.NodeType == ExpressionType.Call) { 
							2676 				 
							2677 				var call = (MethodCallExpression)expr; 
							2678 				var args = new CompileResult[call.Arguments.Count]; 
							2679 				var obj = call.Object != null ? CompileExpr (call.Object, queryArgs) : null; 
							2680 				 
							2681 				for (var i = 0; i < args.Length; i++) { 
								2682 					args [i] = CompileExpr (call.Arguments [i], queryArgs); 
								2683 				} 
							2684 				 
							2685 				var sqlCall = ""; 
							2686 				 
							2687 				if (call.Method.Name == "Like" && args.Length == 2) { 
								2688 					sqlCall = "(" + args [0].CommandText + " like " + args [1].CommandText + ")"; 
								2689 				} 
							2690 				else if (call.Method.Name == "Contains" && args.Length == 2) { 
								2691 					sqlCall = "(" + args [1].CommandText + " in " + args [0].CommandText + ")"; 
								2692 				} 
							2693 				else if (call.Method.Name == "Contains" && args.Length == 1) { 
								2694 					if (call.Object != null && call.Object.Type == typeof(string)) { 
									2695 						sqlCall = "(" + obj.CommandText + " like ('%' || " + args [0].CommandText + " || '%'))"; 
									2696 					} 
								2697 					else { 
									2698 						sqlCall = "(" + args [0].CommandText + " in " + obj.CommandText + ")"; 
									2699 					} 
								2700 				} 
							2701 				else if (call.Method.Name == "StartsWith" && args.Length == 1) { 
								2702 					sqlCall = "(" + obj.CommandText + " like (" + args [0].CommandText + " || '%'))"; 
								2703 				} 
							2704 				else if (call.Method.Name == "EndsWith" && args.Length == 1) { 
								2705 					sqlCall = "(" + obj.CommandText + " like ('%' || " + args [0].CommandText + "))"; 
								2706 				} 
							2707 				else if (call.Method.Name == "Equals" && args.Length == 1) { 
								2708 					sqlCall = "(" + obj.CommandText + " = (" + args[0].CommandText + "))"; 
							2709 				} else if (call.Method.Name == "ToLower") { 
								2710 					sqlCall = "(lower(" + obj.CommandText + "))";  
							2711 				} else if (call.Method.Name == "ToUpper") { 
								2712 					sqlCall = "(upper(" + obj.CommandText + "))";  
							2713 				} else { 
								2714 					sqlCall = call.Method.Name.ToLower () + "(" + string.Join (",", args.Select (a => a.CommandText).ToArray ()) + ")"; 
								2715 				} 
							2716 				return new CompileResult { CommandText = sqlCall }; 
							2717 				 
						2718 			} else if (expr.NodeType == ExpressionType.Constant) { 
							2719 				var c = (ConstantExpression)expr; 
							2720 				queryArgs.Add (c.Value); 
							2721 				return new CompileResult { 
								2722 					CommandText = "?", 
								2723 					Value = c.Value 
									2724 				}; 
						2725 			} else if (expr.NodeType == ExpressionType.Convert) { 
							2726 				var u = (UnaryExpression)expr; 
							2727 				var ty = u.Type; 
							2728 				var valr = CompileExpr (u.Operand, queryArgs); 
							2729 				return new CompileResult { 
								2730 					CommandText = valr.CommandText, 
								2731 					Value = valr.Value != null ? ConvertTo (valr.Value, ty) : null 
									2732 				}; 
						2733 			} else if (expr.NodeType == ExpressionType.MemberAccess) { 
							2734 				var mem = (MemberExpression)expr; 
							2735 				 
							2736 				if (mem.Expression!=null && mem.Expression.NodeType == ExpressionType.Parameter) { 
								2737 					// 
								2738 					// This is a column of our table, output just the column name 
								2739 					// Need to translate it if that column name is mapped 
								2740 					// 
								2741 					var columnName = Table.FindColumnWithPropertyName (mem.Member.Name).Name; 
								2742 					return new CompileResult { CommandText = "\"" + columnName + "\"" }; 
							2743 				} else { 
								2744 					object obj = null; 
								2745 					if (mem.Expression != null) { 
									2746 						var r = CompileExpr (mem.Expression, queryArgs); 
									2747 						if (r.Value == null) { 
										2748 							throw new NotSupportedException ("Member access failed to compile expression"); 
										2749 						} 
									2750 						if (r.CommandText == "?") { 
										2751 							queryArgs.RemoveAt (queryArgs.Count - 1); 
										2752 						} 
									2753 						obj = r.Value; 
									2754 					} 
								2755 					 
								2756 					// 
								2757 					// Get the member value 
								2758 					// 
								2759 					object val = null; 
								2760 					 
								2761 #if !USE_NEW_REFLECTION_API 
								2762 					if (mem.Member.MemberType == MemberTypes.Property) { 
									2763 #else 
										2764 					if (mem.Member is PropertyInfo) { 
											2765 #endif 
											2766 						var m = (PropertyInfo)mem.Member; 
											2767 						val = m.GetValue (obj, null); 
											2768 #if !USE_NEW_REFLECTION_API 
										2769 					} else if (mem.Member.MemberType == MemberTypes.Field) { 
											2770 #else 
										2771 					} else if (mem.Member is FieldInfo) { 
											2772 #endif 
											2773 #if SILVERLIGHT 
												2774 						val = Expression.Lambda (expr).Compile ().DynamicInvoke (); 
											2775 #else 
												2776 						var m = (FieldInfo)mem.Member; 
											2777 						val = m.GetValue (obj); 
											2778 #endif 
										2779 					} else { 
											2780 #if !USE_NEW_REFLECTION_API 
												2781 						throw new NotSupportedException ("MemberExpr: " + mem.Member.MemberType); 
											2782 #else 
												2783 						throw new NotSupportedException ("MemberExpr: " + mem.Member.DeclaringType); 
											2784 #endif 
											2785 					} 
									2786 					 
									2787 					// 
									2788 					// Work special magic for enumerables 
									2789 					// 
									2790 					if (val != null && val is System.Collections.IEnumerable && !(val is string) && !(val is System.Collections.Generic.IEnumerable<byte>)) { 
										2791 						var sb = new System.Text.StringBuilder(); 
										2792 						sb.Append("("); 
										2793 						var head = ""; 
										2794 						foreach (var a in (System.Collections.IEnumerable)val) { 
											2795 							queryArgs.Add(a); 
											2796 							sb.Append(head); 
											2797 							sb.Append("?"); 
											2798 							head = ","; 
											2799 						} 
										2800 						sb.Append(")"); 
										2801 						return new CompileResult { 
											2802 							CommandText = sb.ToString(), 
											2803 							Value = val 
												2804 						}; 
										2805 					} 
									2806 					else { 
										2807 						queryArgs.Add (val); 
										2808 						return new CompileResult { 
											2809 							CommandText = "?", 
											2810 							Value = val 
												2811 						}; 
										2812 					} 
									2813 				} 
								2814 			} 
							2815 			throw new NotSupportedException ("Cannot compile: " + expr.NodeType.ToString ()); 
							2816 		} 
						2817 

						2818 		static object ConvertTo (object obj, Type t) 
						2819 		{ 
							2820 			Type nut = Nullable.GetUnderlyingType(t); 
							2821 			 
							2822 			if (nut != null) { 
								2823 				if (obj == null) return null;				 
								2824 				return Convert.ChangeType (obj, nut); 
							2825 			} else { 
								2826 				return Convert.ChangeType (obj, t); 
								2827 			} 
							2828 		} 
						2829 

						2830 		/// <summary> 
						2831 		/// Compiles a BinaryExpression where one of the parameters is null. 
						2832 		/// </summary> 
						2833 		/// <param name="parameter">The non-null parameter</param> 
						2834 		private string CompileNullBinaryExpression(BinaryExpression expression, CompileResult parameter) 
						2835 		{ 
							2836 			if (expression.NodeType == ExpressionType.Equal) 
								2837 				return "(" + parameter.CommandText + " is ?)"; 
							2838 			else if (expression.NodeType == ExpressionType.NotEqual) 
								2839 				return "(" + parameter.CommandText + " is not ?)"; 
							2840 			else 
								2841 				throw new NotSupportedException("Cannot compile Null-BinaryExpression with type " + expression.NodeType.ToString()); 
							2842 		} 
						2843 

						2844 		string GetSqlName (Expression expr) 
						2845 		{ 
							2846 			var n = expr.NodeType; 
							2847 			if (n == ExpressionType.GreaterThan) 
							2848 				return ">"; else if (n == ExpressionType.GreaterThanOrEqual) { 
								2849 				return ">="; 
							2850 			} else if (n == ExpressionType.LessThan) { 
								2851 				return "<"; 
							2852 			} else if (n == ExpressionType.LessThanOrEqual) { 
								2853 				return "<="; 
							2854 			} else if (n == ExpressionType.And) { 
								2855 				return "&"; 
							2856 			} else if (n == ExpressionType.AndAlso) { 
								2857 				return "and"; 
							2858 			} else if (n == ExpressionType.Or) { 
								2859 				return "|"; 
							2860 			} else if (n == ExpressionType.OrElse) { 
								2861 				return "or"; 
							2862 			} else if (n == ExpressionType.Equal) { 
								2863 				return "="; 
							2864 			} else if (n == ExpressionType.NotEqual) { 
								2865 				return "!="; 
							2866 			} else { 
								2867 				throw new NotSupportedException ("Cannot get SQL for: " + n); 
								2868 			} 
							2869 		} 
						2870 		 
						2871 		public int Count () 
						2872 		{ 
							2873 			return GenerateCommand("count(*)").ExecuteScalar<int> ();			 
							2874 		} 
						2875 

						2876 		public int Count (Expression<Func<T, bool>> predExpr) 
						2877 		{ 
							2878 			return Where (predExpr).Count (); 
							2879 		} 
						2880 

						2881 		public IEnumerator<T> GetEnumerator () 
						2882 		{ 
							2883 			if (!_deferred) 
								2884 				return GenerateCommand("*").ExecuteQuery<T>().GetEnumerator(); 
							2885 

							2886 			return GenerateCommand("*").ExecuteDeferredQuery<T>().GetEnumerator(); 
							2887 		} 
						2888 

						2889 		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () 
						2890 		{ 
							2891 			return GetEnumerator (); 
							2892 		} 
						2893 

						2894 		public T First () 
						2895 		{ 
							2896 			var query = Take (1); 
							2897 			return query.ToList<T>().First (); 
							2898 		} 
						2899 

						2900 		public T FirstOrDefault () 
						2901 		{ 
							2902 			var query = Take (1); 
							2903 			return query.ToList<T>().FirstOrDefault (); 
							2904 		} 
						2905     } 
					2906 

					2907 	public static class SQLite3 
					2908 	{ 
						2909 		public enum Result : int 
						2910 		{ 
							2911 			OK = 0, 
							2912 			Error = 1, 
							2913 			Internal = 2, 
							2914 			Perm = 3, 
							2915 			Abort = 4, 
							2916 			Busy = 5, 
							2917 			Locked = 6, 
							2918 			NoMem = 7, 
							2919 			ReadOnly = 8, 
							2920 			Interrupt = 9, 
							2921 			IOError = 10, 
							2922 			Corrupt = 11, 
							2923 			NotFound = 12, 
							2924 			Full = 13, 
							2925 			CannotOpen = 14, 
							2926 			LockErr = 15, 
							2927 			Empty = 16, 
							2928 			SchemaChngd = 17, 
							2929 			TooBig = 18, 
							2930 			Constraint = 19, 
							2931 			Mismatch = 20, 
							2932 			Misuse = 21, 
							2933 			NotImplementedLFS = 22, 
							2934 			AccessDenied = 23, 
							2935 			Format = 24, 
							2936 			Range = 25, 
							2937 			NonDBFile = 26, 
							2938 			Notice = 27, 
							2939 			Warning = 28, 
							2940 			Row = 100, 
							2941 			Done = 101 
								2942 		} 
						2943 

						2944 		public enum ExtendedResult : int 
						2945 		{ 
							2946 			IOErrorRead = (Result.IOError | (1 << 8)), 
							2947 			IOErrorShortRead = (Result.IOError | (2 << 8)), 
							2948 			IOErrorWrite = (Result.IOError | (3 << 8)), 
							2949 			IOErrorFsync = (Result.IOError | (4 << 8)), 
							2950 			IOErrorDirFSync = (Result.IOError | (5 << 8)), 
							2951 			IOErrorTruncate = (Result.IOError | (6 << 8)), 
							2952 			IOErrorFStat = (Result.IOError | (7 << 8)), 
							2953 			IOErrorUnlock = (Result.IOError | (8 << 8)), 
							2954 			IOErrorRdlock = (Result.IOError | (9 << 8)), 
							2955 			IOErrorDelete = (Result.IOError | (10 << 8)), 
							2956 			IOErrorBlocked = (Result.IOError | (11 << 8)), 
							2957 			IOErrorNoMem = (Result.IOError | (12 << 8)), 
							2958 			IOErrorAccess = (Result.IOError | (13 << 8)), 
							2959 			IOErrorCheckReservedLock = (Result.IOError | (14 << 8)), 
							2960 			IOErrorLock = (Result.IOError | (15 << 8)), 
							2961 			IOErrorClose = (Result.IOError | (16 << 8)), 
							2962 			IOErrorDirClose = (Result.IOError | (17 << 8)), 
							2963 			IOErrorSHMOpen = (Result.IOError | (18 << 8)), 
							2964 			IOErrorSHMSize = (Result.IOError | (19 << 8)), 
							2965 			IOErrorSHMLock = (Result.IOError | (20 << 8)), 
							2966 			IOErrorSHMMap = (Result.IOError | (21 << 8)), 
							2967 			IOErrorSeek = (Result.IOError | (22 << 8)), 
							2968 			IOErrorDeleteNoEnt = (Result.IOError | (23 << 8)), 
							2969 			IOErrorMMap = (Result.IOError | (24 << 8)), 
							2970 			LockedSharedcache = (Result.Locked | (1 << 8)), 
							2971 			BusyRecovery = (Result.Busy | (1 << 8)), 
							2972 			CannottOpenNoTempDir = (Result.CannotOpen | (1 << 8)), 
							2973 			CannotOpenIsDir = (Result.CannotOpen | (2 << 8)), 
							2974 			CannotOpenFullPath = (Result.CannotOpen | (3 << 8)), 
							2975 			CorruptVTab = (Result.Corrupt | (1 << 8)), 
							2976 			ReadonlyRecovery = (Result.ReadOnly | (1 << 8)), 
							2977 			ReadonlyCannotLock = (Result.ReadOnly | (2 << 8)), 
							2978 			ReadonlyRollback = (Result.ReadOnly | (3 << 8)), 
							2979 			AbortRollback = (Result.Abort | (2 << 8)), 
							2980 			ConstraintCheck = (Result.Constraint | (1 << 8)), 
							2981 			ConstraintCommitHook = (Result.Constraint | (2 << 8)), 
							2982 			ConstraintForeignKey = (Result.Constraint | (3 << 8)), 
							2983 			ConstraintFunction = (Result.Constraint | (4 << 8)), 
							2984 			ConstraintNotNull = (Result.Constraint | (5 << 8)), 
							2985 			ConstraintPrimaryKey = (Result.Constraint | (6 << 8)), 
							2986 			ConstraintTrigger = (Result.Constraint | (7 << 8)), 
							2987 			ConstraintUnique = (Result.Constraint | (8 << 8)), 
							2988 			ConstraintVTab = (Result.Constraint | (9 << 8)), 
							2989 			NoticeRecoverWAL = (Result.Notice | (1 << 8)), 
							2990 			NoticeRecoverRollback = (Result.Notice | (2 << 8)) 
								2991 		} 
						2992          
						2993 

						2994 		public enum ConfigOption : int 
						2995 		{ 
							2996 			SingleThread = 1, 
							2997 			MultiThread = 2, 
							2998 			Serialized = 3 
								2999 		} 
						3000 

						3001 #if !USE_CSHARP_SQLITE && !USE_WP8_NATIVE_SQLITE && !USE_SQLITEPCL_RAW 
							3002 		[DllImport("sqlite3", EntryPoint = "sqlite3_threadsafe", CallingConvention=CallingConvention.Cdecl)] 
							3003 		public static extern int Threadsafe (); 
						3004 

						3005 		[DllImport("sqlite3", EntryPoint = "sqlite3_open", CallingConvention=CallingConvention.Cdecl)] 
						3006 		public static extern Result Open ([MarshalAs(UnmanagedType.LPStr)] string filename, out IntPtr db); 
						3007 

						3008 		[DllImport("sqlite3", EntryPoint = "sqlite3_open_v2", CallingConvention=CallingConvention.Cdecl)] 
						3009 		public static extern Result Open ([MarshalAs(UnmanagedType.LPStr)] string filename, out IntPtr db, int flags, IntPtr zvfs); 
						3010 		 
						3011 		[DllImport("sqlite3", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)] 
						3012 		public static extern Result Open(byte[] filename, out IntPtr db, int flags, IntPtr zvfs); 
						3013 

						3014 		[DllImport("sqlite3", EntryPoint = "sqlite3_open16", CallingConvention = CallingConvention.Cdecl)] 
						3015 		public static extern Result Open16([MarshalAs(UnmanagedType.LPWStr)] string filename, out IntPtr db); 
						3016 

						3017 		[DllImport("sqlite3", EntryPoint = "sqlite3_enable_load_extension", CallingConvention=CallingConvention.Cdecl)] 
						3018 		public static extern Result EnableLoadExtension (IntPtr db, int onoff); 
						3019 

						3020 		[DllImport("sqlite3", EntryPoint = "sqlite3_close", CallingConvention=CallingConvention.Cdecl)] 
						3021 		public static extern Result Close (IntPtr db); 
						3022 		 
						3023 		[DllImport("sqlite3", EntryPoint = "sqlite3_initialize", CallingConvention=CallingConvention.Cdecl)] 
						3024 		public static extern Result Initialize(); 
						3025 						 
						3026 		[DllImport("sqlite3", EntryPoint = "sqlite3_shutdown", CallingConvention=CallingConvention.Cdecl)] 
						3027 		public static extern Result Shutdown(); 
						3028 		 
						3029 		[DllImport("sqlite3", EntryPoint = "sqlite3_config", CallingConvention=CallingConvention.Cdecl)] 
						3030 		public static extern Result Config (ConfigOption option); 
						3031 

						3032 		[DllImport("sqlite3", EntryPoint = "sqlite3_win32_set_directory", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] 
						3033 		public static extern int SetDirectory (uint directoryType, string directoryPath); 
						3034 

						3035 		[DllImport("sqlite3", EntryPoint = "sqlite3_busy_timeout", CallingConvention=CallingConvention.Cdecl)] 
						3036 		public static extern Result BusyTimeout (IntPtr db, int milliseconds); 
						3037 

						3038 		[DllImport("sqlite3", EntryPoint = "sqlite3_changes", CallingConvention=CallingConvention.Cdecl)] 
						3039 		public static extern int Changes (IntPtr db); 
						3040 

						3041 		[DllImport("sqlite3", EntryPoint = "sqlite3_prepare_v2", CallingConvention=CallingConvention.Cdecl)] 
						3042 		public static extern Result Prepare2 (IntPtr db, [MarshalAs(UnmanagedType.LPStr)] string sql, int numBytes, out IntPtr stmt, IntPtr pzTail); 
						3043 

						3044 #if NETFX_CORE 
							3045 		[DllImport ("sqlite3", EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)] 
							3046 		public static extern Result Prepare2 (IntPtr db, byte[] queryBytes, int numBytes, out IntPtr stmt, IntPtr pzTail); 
						3047 #endif 
						3048 

						3049 		public static IntPtr Prepare2 (IntPtr db, string query) 
						3050 		{ 
							3051 			IntPtr stmt; 
							3052 #if NETFX_CORE 
								3053             byte[] queryBytes = System.Text.UTF8Encoding.UTF8.GetBytes (query); 
							3054             var r = Prepare2 (db, queryBytes, queryBytes.Length, out stmt, IntPtr.Zero); 
							3055 #else 
								3056             var r = Prepare2 (db, query, System.Text.UTF8Encoding.UTF8.GetByteCount (query), out stmt, IntPtr.Zero); 
							3057 #endif 
							3058 			if (r != Result.OK) { 
								3059 				throw SQLiteException.New (r, GetErrmsg (db)); 
								3060 			} 
							3061 			return stmt; 
							3062 		} 
						3063 

						3064 		[DllImport("sqlite3", EntryPoint = "sqlite3_step", CallingConvention=CallingConvention.Cdecl)] 
						3065 		public static extern Result Step (IntPtr stmt); 
						3066 

						3067 		[DllImport("sqlite3", EntryPoint = "sqlite3_reset", CallingConvention=CallingConvention.Cdecl)] 
						3068 		public static extern Result Reset (IntPtr stmt); 
						3069 

						3070 		[DllImport("sqlite3", EntryPoint = "sqlite3_finalize", CallingConvention=CallingConvention.Cdecl)] 
						3071 		public static extern Result Finalize (IntPtr stmt); 
						3072 

						3073 		[DllImport("sqlite3", EntryPoint = "sqlite3_last_insert_rowid", CallingConvention=CallingConvention.Cdecl)] 
						3074 		public static extern long LastInsertRowid (IntPtr db); 
						3075 

						3076 		[DllImport("sqlite3", EntryPoint = "sqlite3_errmsg16", CallingConvention=CallingConvention.Cdecl)] 
						3077 		public static extern IntPtr Errmsg (IntPtr db); 
						3078 

						3079 		public static string GetErrmsg (IntPtr db) 
						3080 		{ 
							3081 			return Marshal.PtrToStringUni (Errmsg (db)); 
							3082 		} 
						3083 

						3084 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_parameter_index", CallingConvention=CallingConvention.Cdecl)] 
						3085 		public static extern int BindParameterIndex (IntPtr stmt, [MarshalAs(UnmanagedType.LPStr)] string name); 
						3086 

						3087 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_null", CallingConvention=CallingConvention.Cdecl)] 
						3088 		public static extern int BindNull (IntPtr stmt, int index); 
						3089 

						3090 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_int", CallingConvention=CallingConvention.Cdecl)] 
						3091 		public static extern int BindInt (IntPtr stmt, int index, int val); 
						3092 

						3093 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_int64", CallingConvention=CallingConvention.Cdecl)] 
						3094 		public static extern int BindInt64 (IntPtr stmt, int index, long val); 
						3095 

						3096 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_double", CallingConvention=CallingConvention.Cdecl)] 
						3097 		public static extern int BindDouble (IntPtr stmt, int index, double val); 
						3098 

						3099 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_text16", CallingConvention=CallingConvention.Cdecl, CharSet = CharSet.Unicode)] 
						3100 		public static extern int BindText (IntPtr stmt, int index, [MarshalAs(UnmanagedType.LPWStr)] string val, int n, IntPtr free); 
						3101 

						3102 		[DllImport("sqlite3", EntryPoint = "sqlite3_bind_blob", CallingConvention=CallingConvention.Cdecl)] 
						3103 		public static extern int BindBlob (IntPtr stmt, int index, byte[] val, int n, IntPtr free); 
						3104 

						3105 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_count", CallingConvention=CallingConvention.Cdecl)] 
						3106 		public static extern int ColumnCount (IntPtr stmt); 
						3107 

						3108 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_name", CallingConvention=CallingConvention.Cdecl)] 
						3109 		public static extern IntPtr ColumnName (IntPtr stmt, int index); 
						3110 

						3111 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_name16", CallingConvention=CallingConvention.Cdecl)] 
						3112 		static extern IntPtr ColumnName16Internal (IntPtr stmt, int index); 
						3113 		public static string ColumnName16(IntPtr stmt, int index) 
						3114 		{ 
							3115 			return Marshal.PtrToStringUni(ColumnName16Internal(stmt, index)); 
							3116 		} 
						3117 

						3118 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_type", CallingConvention=CallingConvention.Cdecl)] 
						3119 		public static extern ColType ColumnType (IntPtr stmt, int index); 
						3120 

						3121 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_int", CallingConvention=CallingConvention.Cdecl)] 
						3122 		public static extern int ColumnInt (IntPtr stmt, int index); 
						3123 

						3124 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_int64", CallingConvention=CallingConvention.Cdecl)] 
						3125 		public static extern long ColumnInt64 (IntPtr stmt, int index); 
						3126 

						3127 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_double", CallingConvention=CallingConvention.Cdecl)] 
						3128 		public static extern double ColumnDouble (IntPtr stmt, int index); 
						3129 

						3130 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_text", CallingConvention=CallingConvention.Cdecl)] 
						3131 		public static extern IntPtr ColumnText (IntPtr stmt, int index); 
						3132 

						3133 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_text16", CallingConvention=CallingConvention.Cdecl)] 
						3134 		public static extern IntPtr ColumnText16 (IntPtr stmt, int index); 
						3135 

						3136 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_blob", CallingConvention=CallingConvention.Cdecl)] 
						3137 		public static extern IntPtr ColumnBlob (IntPtr stmt, int index); 
						3138 

						3139 		[DllImport("sqlite3", EntryPoint = "sqlite3_column_bytes", CallingConvention=CallingConvention.Cdecl)] 
						3140 		public static extern int ColumnBytes (IntPtr stmt, int index); 
						3141 

						3142 		public static string ColumnString (IntPtr stmt, int index) 
						3143 		{ 
							3144 			return Marshal.PtrToStringUni (SQLite3.ColumnText16 (stmt, index)); 
							3145 		} 
						3146 

						3147 		public static byte[] ColumnByteArray (IntPtr stmt, int index) 
						3148 		{ 
							3149 			int length = ColumnBytes (stmt, index); 
							3150 			var result = new byte[length]; 
							3151 			if (length > 0) 
								3152 				Marshal.Copy (ColumnBlob (stmt, index), result, 0, length); 
							3153 			return result; 
							3154 		} 
						3155 

						3156 		[DllImport ("sqlite3", EntryPoint = "sqlite3_extended_errcode", CallingConvention = CallingConvention.Cdecl)] 
						3157 		public static extern ExtendedResult ExtendedErrCode (IntPtr db); 
						3158 

						3159 		[DllImport ("sqlite3", EntryPoint = "sqlite3_libversion_number", CallingConvention = CallingConvention.Cdecl)] 
						3160 		public static extern int LibVersionNumber (); 
						3161 #else 
							3162 		public static Result Open(string filename, out Sqlite3DatabaseHandle db) 
							3163 		{ 
							3164 			return (Result) Sqlite3.sqlite3_open(filename, out db); 
							3165 		} 
						3166 

						3167 		public static Result Open(string filename, out Sqlite3DatabaseHandle db, int flags, IntPtr zVfs) 
						3168 		{ 
							3169 #if USE_WP8_NATIVE_SQLITE 
								3170 			return (Result)Sqlite3.sqlite3_open_v2(filename, out db, flags, ""); 
							3171 #else 
								3172 			return (Result)Sqlite3.sqlite3_open_v2(filename, out db, flags, null); 
							3173 #endif 
							3174 		} 
						3175 

						3176 		public static Result Close(Sqlite3DatabaseHandle db) 
						3177 		{ 
							3178 			return (Result)Sqlite3.sqlite3_close(db); 
							3179 		} 
						3180 

						3181 		public static Result BusyTimeout(Sqlite3DatabaseHandle db, int milliseconds) 
						3182 		{ 
							3183 			return (Result)Sqlite3.sqlite3_busy_timeout(db, milliseconds); 
							3184 		} 
						3185 

						3186 		public static int Changes(Sqlite3DatabaseHandle db) 
						3187 		{ 
							3188 			return Sqlite3.sqlite3_changes(db); 
							3189 		} 
						3190 

						3191 		public static Sqlite3Statement Prepare2(Sqlite3DatabaseHandle db, string query) 
						3192 		{ 
							3193 			Sqlite3Statement stmt = default(Sqlite3Statement); 
							3194 #if USE_WP8_NATIVE_SQLITE || USE_SQLITEPCL_RAW 
								3195 			var r = Sqlite3.sqlite3_prepare_v2(db, query, out stmt); 
							3196 #else 
								3197 			stmt = new Sqlite3Statement(); 
							3198 			var r = Sqlite3.sqlite3_prepare_v2(db, query, -1, ref stmt, 0); 
							3199 #endif 
							3200 			if (r != 0) 
								3201 			{ 
								3202 				throw SQLiteException.New((Result)r, GetErrmsg(db)); 
								3203 			} 
							3204 			return stmt; 
							3205 		} 
						3206 

						3207 		public static Result Step(Sqlite3Statement stmt) 
						3208 		{ 
							3209 			return (Result)Sqlite3.sqlite3_step(stmt); 
							3210 		} 
						3211 

						3212 		public static Result Reset(Sqlite3Statement stmt) 
						3213 		{ 
							3214 			return (Result)Sqlite3.sqlite3_reset(stmt); 
							3215 		} 
						3216 

						3217 		public static Result Finalize(Sqlite3Statement stmt) 
						3218 		{ 
							3219 			return (Result)Sqlite3.sqlite3_finalize(stmt); 
							3220 		} 
						3221 

						3222 		public static long LastInsertRowid(Sqlite3DatabaseHandle db) 
						3223 		{ 
							3224 			return Sqlite3.sqlite3_last_insert_rowid(db); 
							3225 		} 
						3226 

						3227 		public static string GetErrmsg(Sqlite3DatabaseHandle db) 
						3228 		{ 
							3229 			return Sqlite3.sqlite3_errmsg(db); 
							3230 		} 
						3231 

						3232 		public static int BindParameterIndex(Sqlite3Statement stmt, string name) 
						3233 		{ 
							3234 			return Sqlite3.sqlite3_bind_parameter_index(stmt, name); 
							3235 		} 
						3236 

						3237 		public static int BindNull(Sqlite3Statement stmt, int index) 
						3238 		{ 
							3239 			return Sqlite3.sqlite3_bind_null(stmt, index); 
							3240 		} 
						3241 

						3242 		public static int BindInt(Sqlite3Statement stmt, int index, int val) 
						3243 		{ 
							3244 			return Sqlite3.sqlite3_bind_int(stmt, index, val); 
							3245 		} 
						3246 

						3247 		public static int BindInt64(Sqlite3Statement stmt, int index, long val) 
						3248 		{ 
							3249 			return Sqlite3.sqlite3_bind_int64(stmt, index, val); 
							3250 		} 
						3251 

						3252 		public static int BindDouble(Sqlite3Statement stmt, int index, double val) 
						3253 		{ 
							3254 			return Sqlite3.sqlite3_bind_double(stmt, index, val); 
							3255 		} 
						3256 

						3257 		public static int BindText(Sqlite3Statement stmt, int index, string val, int n, IntPtr free) 
						3258 		{ 
							3259 #if USE_WP8_NATIVE_SQLITE 
								3260 			return Sqlite3.sqlite3_bind_text(stmt, index, val, n); 
							3261 #elif USE_SQLITEPCL_RAW 
							3262 			return Sqlite3.sqlite3_bind_text(stmt, index, val); 
							3263 #else 
								3264 			return Sqlite3.sqlite3_bind_text(stmt, index, val, n, null); 
							3265 #endif 
							3266 		} 
						3267 

						3268 		public static int BindBlob(Sqlite3Statement stmt, int index, byte[] val, int n, IntPtr free) 
						3269 		{ 
							3270 #if USE_WP8_NATIVE_SQLITE 
								3271 			return Sqlite3.sqlite3_bind_blob(stmt, index, val, n); 
							3272 #elif USE_SQLITEPCL_RAW 
							3273 			return Sqlite3.sqlite3_bind_blob(stmt, index, val); 
							3274 #else 
								3275 			return Sqlite3.sqlite3_bind_blob(stmt, index, val, n, null); 
							3276 #endif 
							3277 		} 
						3278 

						3279 		public static int ColumnCount(Sqlite3Statement stmt) 
						3280 		{ 
							3281 			return Sqlite3.sqlite3_column_count(stmt); 
							3282 		} 
						3283 

						3284 		public static string ColumnName(Sqlite3Statement stmt, int index) 
						3285 		{ 
							3286 			return Sqlite3.sqlite3_column_name(stmt, index); 
							3287 		} 
						3288 

						3289 		public static string ColumnName16(Sqlite3Statement stmt, int index) 
						3290 		{ 
							3291 			return Sqlite3.sqlite3_column_name(stmt, index); 
							3292 		} 
						3293 

						3294 		public static ColType ColumnType(Sqlite3Statement stmt, int index) 
						3295 		{ 
							3296 			return (ColType)Sqlite3.sqlite3_column_type(stmt, index); 
							3297 		} 
						3298 

						3299 		public static int ColumnInt(Sqlite3Statement stmt, int index) 
						3300 		{ 
							3301 			return Sqlite3.sqlite3_column_int(stmt, index); 
							3302 		} 
						3303 

						3304 		public static long ColumnInt64(Sqlite3Statement stmt, int index) 
						3305 		{ 
							3306 			return Sqlite3.sqlite3_column_int64(stmt, index); 
							3307 		} 
						3308 

						3309 		public static double ColumnDouble(Sqlite3Statement stmt, int index) 
						3310 		{ 
							3311 			return Sqlite3.sqlite3_column_double(stmt, index); 
							3312 		} 
						3313 

						3314 		public static string ColumnText(Sqlite3Statement stmt, int index) 
						3315 		{ 
							3316 			return Sqlite3.sqlite3_column_text(stmt, index); 
							3317 		} 
						3318 

						3319 		public static string ColumnText16(Sqlite3Statement stmt, int index) 
						3320 		{ 
							3321 			return Sqlite3.sqlite3_column_text(stmt, index); 
							3322 		} 
						3323 

						3324 		public static byte[] ColumnBlob(Sqlite3Statement stmt, int index) 
						3325 		{ 
							3326 			return Sqlite3.sqlite3_column_blob(stmt, index); 
							3327 		} 
						3328 

						3329 		public static int ColumnBytes(Sqlite3Statement stmt, int index) 
						3330 		{ 
							3331 			return Sqlite3.sqlite3_column_bytes(stmt, index); 
							3332 		} 
						3333 

						3334 		public static string ColumnString(Sqlite3Statement stmt, int index) 
						3335 		{ 
							3336 			return Sqlite3.sqlite3_column_text(stmt, index); 
							3337 		} 
						3338 

						3339 		public static byte[] ColumnByteArray(Sqlite3Statement stmt, int index) 
						3340 		{ 
							3341 			return ColumnBlob(stmt, index); 
							3342 		} 
						3343 

						3344 #if !USE_SQLITEPCL_RAW 
							3345 		public static Result EnableLoadExtension(Sqlite3DatabaseHandle db, int onoff) 
							3346 		{ 
							3347 			return (Result)Sqlite3.sqlite3_enable_load_extension(db, onoff); 
							3348 		} 
						3349 #endif 
						3350 

						3351 		public static ExtendedResult ExtendedErrCode(Sqlite3DatabaseHandle db) 
						3352 		{ 
							3353 			return (ExtendedResult)Sqlite3.sqlite3_extended_errcode(db); 
							3354 		} 
						3355 #endif 
						3356 

						3357 		public enum ColType : int 
						3358 		{ 
							3359 			Integer = 1, 
							3360 			Float = 2, 
							3361 			Text = 3, 
							3362 			Blob = 4, 
							3363 			Null = 5 
								3364 		} 
						3365 	} 
					3366 }
	}

}

