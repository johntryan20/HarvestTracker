using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;

namespace HarvestTracker
{
	[Activity (Label = "HarvestTracker", MainLauncher = true, Icon = "@drawable/icon")]
	public class MainActivity : Activity
	{
		int count = 1;

		protected override void OnCreate (Bundle bundle)
		{
			base.OnCreate (bundle);

			// Set our view from the "main" layout resource
			SetContentView (Resource.Layout.Main);

			// Get our button from the layout resource,
			// and attach an event to it
			Button buttonAdd = FindViewById<Button> (Resource.Id.buttonAdd);
			Button buttonRemove = FindViewById<Button> (Resource.Id.buttonRemove);
			Spinner spinnerFieldList = FindViewById<Spinner> (Resource.Id.spinnerFieldList);
			Spinner spinnerEquipmentList = FindViewById<Spinner> (Resource.Id.spinnerEquipmentist);

			// Set up Equipment List Drop down list to use equipment_list in strings.xml
			spinnerEquipmentList.ItemSelected += new EventHandler<AdapterView.ItemSelectedEventArgs> (spinner_itemSelected);
			var adapterEquipmentList = ArrayAdapter.CreateFromResource (this, Resource.Array.equipment_list, Android.Resource.Layout.SimpleSpinnerItem);
			adapterEquipmentList.SetDropDownViewResource (Android.Resource.Layout.SimpleSpinnerDropDownItem);
			spinnerEquipmentList.Adapter = adapterEquipmentList;

			// Set up Field List Drop down list to use field_list in strings.xml
			spinnerFieldList.ItemSelected += new EventHandler<AdapterView.ItemSelectedEventArgs> (spinner_itemSelected);
			var adapterFieldList = ArrayAdapter.CreateFromResource (this, Resource.Array.field_list, Android.Resource.Layout.SimpleSpinnerItem);
			adapterFieldList.SetDropDownViewResource (Android.Resource.Layout.SimpleSpinnerDropDownItem);
			spinnerFieldList.Adapter = adapterFieldList;
		}
		private void spinner_itemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
		{
			Spinner spinner = (Spinner)sender;
			string toast = string.Format("Selected {0}",spinner.GetItemAtPosition(e.Position));
			Toast.MakeText(this,toast,ToastLength.Long).Show();
		}
	
	}
}


