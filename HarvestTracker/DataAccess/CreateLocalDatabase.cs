using System;
using SQLite;
using System.IO;

namespace HarvestTracker
{
	public class DataOperation
	{
		private string _pathToDatabase;

		/// <summary>
		/// ctor
		/// </summary>
		public DataOperation ()
		{
			var documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
			_pathToDatabase = Path.Combine(documents, "db_sqlite-net.db");
		}

		/// <summary>
		/// Create a database and table to hold Location info
		/// </summary>
		public void CreateLocationTable()
		{

			using (var conn= new SQLite.SQLiteConnection(_pathToDatabase,false))
			{
				conn.CreateTable<Location>();
			}
		}

		/// <summary>
		/// Add a new location 
		/// </summary>
		/// <param name="equipmentId">Equipment identifier.</param>
		/// <param name="latitude">Latitude.</param>
		/// <param name="longitude">Longitude.</param>
		/// <param name="isLoadComplete">If set to <c>true</c> is load complete.</param>
		public void AddLocationRecord(int equipmentId,double latitude,double longitude, bool isLoadComplete)
		{
			var location = new Location { EquipmentId = equipmentId, Latitude = latitude, Longitude = longitude, IsLoadComplete = isLoadComplete};

			using (var conn = new SQLite.SQLiteConnection(_pathToDatabase,false))
			{
				conn.Insert (location); 
			}
		}

		/// <summary>
		/// Returns all records from location table
		/// </summary>
		public void SelectAllLocationRecords()
		{
			using (var conn = new SQLite.SQLiteConnection (_pathToDatabase,false)) 
			{
				var queryAllRecords = conn.Query<Location>("SELECT * FROM Location ORDER BY CreatedDate");

				foreach (var v in queryAllRecords) 
				{
					Console.WriteLine (v.Latitude.ToString() + ":" + v.Longitude.ToString());
				}
			};
		}

	}
}
