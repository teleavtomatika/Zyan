﻿using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using Zyan.Communication.Delegates;
using Zyan.Communication.SessionMgmt;
using Zyan.Communication.Toolbox;

namespace Zyan.Communication
{
	/// <summary>
	/// Describes a server session.
	/// </summary>
	[Serializable]
	public class ServerSession
	{
		private Guid _sessionID;
		private IIdentity _identity;
		private DateTime _timestamp;
		private string _clientAddress;
		private SubscriptionTracker _remoteSubscriptionTracker = new SubscriptionTracker();
		private static string _serverSessionSlotName = Guid.NewGuid().ToString();

		// Adapter for accessing session variables.
		[NonSerialized]
		private SessionVariableAdapter _sessionVariableAdapter = null;

		/// <summary>
		/// Creates a new instance of the ServerSession class.
		/// </summary>
		/// <param name="sessionID">Session ID.</param>
		/// <param name="timestamp">Session time stamp.</param>
		/// <param name="identity">Client identity.</param>
		/// <param name="sessionVariableAdapter">Adapter for accessing session variables.</param>
		internal ServerSession(Guid sessionID, DateTime timestamp, IIdentity identity, SessionVariableAdapter sessionVariableAdapter)
		{
			_timestamp = timestamp;
			_sessionID = sessionID;
			_identity = identity;
			_sessionVariableAdapter = sessionVariableAdapter;
		}

		/// <summary>
		/// Gets the session ID.
		/// </summary>
		public Guid SessionID
		{
			get { return _sessionID; }
		}

		/// <summary>
		/// Gets the identity of the client.
		/// </summary>
		public IIdentity Identity
		{
			get { return _identity; }
		}

		/// <summary>
		/// Gets or sets the timestamp of the session.
		/// </summary>
		public DateTime Timestamp
		{
			get { return _timestamp; }
			set { _timestamp = value; }
		}

		/// <summary>
		/// Gets the adapter for accessing session variables.
		/// </summary>
		public ISessionVariableAdapter SessionVariables
		{
			get { return _sessionVariableAdapter; }
		}

		/// <summary>
		/// Gets or sets the IP Address of the calling client.
		/// </summary>
		public string ClientAddress
		{
			get { return _clientAddress; }
			set { _clientAddress = value; }
		}

		/// <summary>
		/// Gets the remote subscription tracker.
		/// </summary>
		internal SubscriptionTracker RemoteSubscriptionTracker
		{
			get { return _remoteSubscriptionTracker; }
		}

		/// <summary>
		/// Adds remote subscriptions to the tracker.
		/// </summary>
		internal string TrackRemoteSubscriptions(IEnumerable<DelegateCorrelationInfo> delegateCorrelationSet)
		{
 			return RemoteSubscriptionTracker.Add(delegateCorrelationSet);
		}

		/// <summary>
		/// Removes remote subscriptions from the tracker.
		/// </summary>
		internal string UntrackRemoteSubscriptions(IEnumerable<DelegateCorrelationInfo> delegateCorrelationSet)
		{
 			return RemoteSubscriptionTracker.Remove(delegateCorrelationSet);
		}

		/// <summary>
		/// Gets the session of the current logical server thread.
		/// </summary>
		/// <remarks>
		/// This property doesn't cross application domain boundaries.
		/// </remarks>
		public static ServerSession CurrentSession
		{
			get { return LocalCallContextData.GetData<ServerSession>(_serverSessionSlotName); }
			internal set { LocalCallContextData.SetData(_serverSessionSlotName, value); }
		}
	}
}
