
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace HarvestTracker
{
	[Activity (Label = "HarvestTracker")]			
	public class HarvestTracker : Activity
	{

		private DataOperation _localDb = new DataOperation ();

		protected override void OnCreate (Bundle bundle)
		{
			base.OnCreate (bundle);

			// Create location table
			_localDb.CreateLocationTable ();

			// Now Start tracking location
			this.StartTrackingLocation ();
		}

		/// <summary>
		/// Start Recording Current Location
		/// </summary>
		private void StartTrackingLocation()
		{
			// Track our current location
			CurrentLocation currentLocation = new CurrentLocation ();
			_localDb.AddLocationRecord(1,currentLocation.Latitude,currentLocation.Longitude, false);
		}

		/// <summary>
		/// Returns All Location data
		/// </summary>
		private void ReturnAllLocationData()
		{
			_localDb.SelectAllLocationRecords ();
		}

	}
}

