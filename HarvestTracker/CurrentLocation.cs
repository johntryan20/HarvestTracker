
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
using Android.Locations;

namespace HarvestTracker
{
	[Activity (Label = "CurrentLocation")]			
	public class CurrentLocation : Activity, ILocationListener 
	{
	
		public double Latitude { get; set;}
		public double Longitude { get; set;}

		LocationManager _locationManger;

		protected override void OnCreate (Bundle bundle)
		{
			base.OnCreate (bundle);
			_locationManger = GetSystemService (Context.LocationService) as Android.Locations.LocationManager;
		}

		public void OnProviderEnabled(string provider)
		{

		}

		public void OnProviderDisabled(string provider)
		{

		}

		public void OnStatusChanged(string provider, Availability status, Bundle extras)
		{
		}

		// Change our Lat/Long position
		public void OnLocationChanged(Android.Locations.Location location)
		{
			this.Latitude = location.Latitude;
			this.Longitude = location.Longitude;
		}

		// Stop Sending Location Service Updates to app
		public void onPause()
		{
			base.OnPause ();
			_locationManger.RemoveUpdates (this); 
		}

	}
}

