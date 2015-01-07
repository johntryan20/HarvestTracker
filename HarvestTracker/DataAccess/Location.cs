using System;
using Mono.Data.Sqlite; 
using SQLite;
namespace HarvestTracker
{
	public class Location
	{
		[PrimaryKey, AutoIncrement]
		public int ID { get; set; }
		public DateTime CreatedDate { get; set; }
		public int EquipmentId {get; set;}
		public bool IsLoadComplete {get; set;}

		public double Latitude { get; set; }
		public double Longitude { get; set; }

		public override string ToString ()
		{
			return string.Format ("[Location: ID={0}, EquipmentId={1}, Latitude={2}, Longitude={3}, IsLoadComplete={4}]", ID, EquipmentId, Latitude, Longitude, IsLoadComplete);
		}

	}
}

