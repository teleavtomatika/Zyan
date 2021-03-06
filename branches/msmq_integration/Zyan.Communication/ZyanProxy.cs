﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Reflection;
using System.Transactions;

namespace Zyan.Communication
{
    /// <summary>
    /// Stellvertreterobjekt für den Zugriff auf eine entfernte Komponente.
    /// </summary>
    public class ZyanProxy : RealProxy
    {
        // Felder
        private Type _interfaceType = null;
        private IZyanDispatcher _remoteInvoker = null;
        private List<DelegateCorrelationInfo> _delegateCorrelationSet = null;
        private bool _implicitTransactionTransfer = false;
        private Guid _sessionID;
        private string _componentHostName = string.Empty;
        private bool _autoLoginOnExpiredSession = false;
        private Hashtable _autoLoginCredentials = null;
        private ZyanConnection _connection = null;
        private ActivationType _activationType = ActivationType.SingleCall;

        /// <summary>
        /// Konstruktor.
        /// </summary>
        /// <param name="type">Schnittstelle der entfernten Komponente</param>
        /// <param name="connection">Verbindungsobjekt</param>
        /// <param name="implicitTransactionTransfer">Implizite Transaktionsübertragung</param>
        /// <param name="sessionID">Sitzungsschlüssel</param>
        /// <param name="componentHostName">Name des entfernten Komponentenhosts</param>
        /// <param name="autoLoginOnExpiredSession">Gibt an, ob sich der Proxy automatisch neu anmelden soll, wenn die Sitzung abgelaufen ist</param>
        /// <param name="autoLogoninCredentials">Optional! Anmeldeinformationen, die nur benötigt werden, wenn autoLoginOnExpiredSession auf Wahr eingestellt ist</param>              
        /// <param name="activationType">Aktivierungsart</param>
        public ZyanProxy(Type type, ZyanConnection connection, bool implicitTransactionTransfer, Guid sessionID, string componentHostName, bool autoLoginOnExpiredSession, Hashtable autoLogoninCredentials, ActivationType activationType)
            : base(type)
        {
            // Wenn kein Typ angegeben wurde ...
            if (type.Equals(null))
                // Ausnahme werfen
                throw new ArgumentNullException("type");

            // Wenn kein Verbindungsobjekt angegeben wurde ...
            if (connection == null)
                // Ausnahme werfen
                throw new ArgumentNullException("connection");

            // Sitzungsschlüssel übernehmen
            _sessionID = sessionID;

            // Verbindungsobjekt übernehmen
            _connection = connection;

            // Name des Komponentenhosts übernehmen
            _componentHostName = componentHostName;

            // Schnittstellentyp übernehmen
            _interfaceType = type;

            // Aktivierungsart übernehmen
            _activationType = activationType;

            // Aufrufer von Verbindung übernehmen
            _remoteInvoker = _connection.RemoteComponentFactory;

            // Schalter für implizite Transaktionsübertragung übernehmen
            _implicitTransactionTransfer = implicitTransactionTransfer;

            // Schalter für automatische Anmeldung bei abgelaufender Sitzung übernehmen
            _autoLoginOnExpiredSession = autoLoginOnExpiredSession;

            // Wenn automatische Anmeldung aktiv ist ...
            if (_autoLoginOnExpiredSession)
                // Anmeldeinformationen speichern
                _autoLoginCredentials = autoLogoninCredentials;

            // Sammlung für Korrelationssatz erzeugen
            _delegateCorrelationSet = new List<DelegateCorrelationInfo>();
        }

        /// <summary>
        /// Entfernte Methode aufrufen.
        /// </summary>
        /// <param name="message">Remoting-Nachricht mit Details für den entfernten Methodenaufruf</param>
        /// <returns>Remoting Antwortnachricht</returns>
        public override IMessage Invoke(IMessage message)
        {
            // Wenn keine Nachricht angegeben wurde ...
            if (message == null)
                // Ausnahme werfen
                throw new ArgumentNullException("message");

            // Nachricht in benötigte Schnittstelle casten
            IMethodCallMessage methodCallMessage = (IMethodCallMessage)message;

            // Methoden-Metadaten abrufen
            MethodInfo methodInfo = (MethodInfo)methodCallMessage.MethodBase;
            methodInfo.GetParameters();

            // Wenn die Methode ein Delegat ist ...
            if (methodInfo.ReturnType.Equals(typeof(void)) &&
                methodCallMessage.InArgCount == 1 &&
                methodCallMessage.ArgCount == 1 &&
                methodCallMessage.Args[0] != null &&
                typeof(Delegate).IsAssignableFrom(methodCallMessage.Args[0].GetType()) &&
                (methodCallMessage.MethodName.StartsWith("set_") || methodCallMessage.MethodName.StartsWith("add_")))
            {
                // Delegat auf zu verdrahtende Client-Methode abrufen
                object receiveMethodDelegate = methodCallMessage.GetArg(0);

                // "set_" wegschneiden
                string propertyName = methodCallMessage.MethodName.Substring(4);

                // Verdrahtungskonfiguration festschreiben
                DelegateInterceptor wiring = new DelegateInterceptor()
                {
                    ClientDelegate = receiveMethodDelegate                
                };
                // Korrelationsinformation zusammenstellen
                DelegateCorrelationInfo correlationInfo = new DelegateCorrelationInfo()
                {
                    IsEvent = methodCallMessage.MethodName.StartsWith("add_"),
                    DelegateMemberName = propertyName,
                    ClientDelegateInterceptor=wiring
                };
                // Wenn die Serverkomponente Singletonaktiviert ist ...
                if (_activationType == ActivationType.Singleton)                    
                    // Ereignis der Serverkomponente abonnieren
                    _connection.RemoteComponentFactory.AddEventHandler(_interfaceType.FullName, correlationInfo);

                // Verdrahtung in der Sammlung ablegen
                _delegateCorrelationSet.Add(correlationInfo);
                
                // Leere Remoting-Antwortnachricht erstellen und zurückgeben
                return new ReturnMessage(null, null, 0, methodCallMessage.LogicalCallContext, methodCallMessage);
            }
            else if (methodInfo.ReturnType.Equals(typeof(void)) &&
                methodCallMessage.InArgCount == 1 &&
                methodCallMessage.ArgCount == 1 &&
                methodCallMessage.Args[0] != null &&
                typeof(Delegate).IsAssignableFrom(methodCallMessage.Args[0].GetType()) &&
                (methodCallMessage.MethodName.StartsWith("remove_")))
            {
                // EBC-Eingangsnachricht abrufen
                object inputMessage = methodCallMessage.GetArg(0);

                // "remove_" wegschneiden
                string propertyName = methodCallMessage.MethodName.Substring(7);

                // Wenn Verdrahtungen gespeichert sind ...
                if (_delegateCorrelationSet.Count > 0)
                {
                    // Verdrahtungskonfiguration suchen
                    DelegateCorrelationInfo found = (from correlationInfo in (DelegateCorrelationInfo[])_delegateCorrelationSet.ToArray()
                                                     where correlationInfo.DelegateMemberName.Equals(propertyName) && correlationInfo.ClientDelegateInterceptor.ClientDelegate.Equals(inputMessage)
                                                     select correlationInfo).FirstOrDefault();

                    // Wenn eine passende Verdrahtungskonfiguration gefunden wurde ...
                    if (found != null)
                    {
                        // Wenn die Serverkomponente SingleCallaktiviert ist ...
                        if (_activationType == ActivationType.SingleCall)
                        {
                            // Verdrahtungskonfiguration entfernen
                            _delegateCorrelationSet.Remove(found);
                        }
                        else
                        {
                            // Ereignisabo entfernen
                            _connection.RemoteComponentFactory.RemoveEventHandler(_interfaceType.FullName, found);
                        }
                    }
                }
                // Leere Remoting-Antwortnachricht erstellen und zurückgeben
                return new ReturnMessage(null, null, 0, methodCallMessage.LogicalCallContext, methodCallMessage);
            }
            else
            {
                // Aufrufkontext vorbereiten
                _connection.PrepareCallContext(_implicitTransactionTransfer);

                // Entfernten Methodenaufruf durchführen und jede Antwortnachricht sofort über einen Rückkanal empfangen
                return InvokeRemoteMethod(methodCallMessage);
            }
        }

        /// <summary>
        /// Führt einen entfernten Methodenaufruf aus.
        /// </summary>
        /// <param name="methodCallMessage">Remoting-Nachricht mit Details für den entfernten Methodenaufruf</param>
        /// <returns>Remoting Antwortnachricht</returns>
        private IMessage InvokeRemoteMethod(IMethodCallMessage methodCallMessage)
        {
            // Aufrufschlüssel vergeben
            Guid trackingID = Guid.NewGuid();

            try
            {
                // Variable für Rückgabewert
                object returnValue = null;

                // Variable für Verdrahtungskorrelationssatz
                List<DelegateCorrelationInfo> correlationSet = null;

                // Wenn die Komponente SingleCallaktiviert ist ...
                if (_activationType == ActivationType.SingleCall)
                    // Korrelationssatz übernehmen (wird mit übertragen)
                    correlationSet = _delegateCorrelationSet;

                // Ereignisargumente für BeforeInvoke erstellen
                BeforeInvokeEventArgs cancelArgs = new BeforeInvokeEventArgs()
                {
                    TrackingID = trackingID,
                    InterfaceName = _interfaceType.FullName,
                    DelegateCorrelationSet = correlationSet,
                    MethodName = methodCallMessage.MethodName,
                    Arguments = methodCallMessage.Args,
                    Cancel = false
                };
                // BeforeInvoke-Ereignis feuern
                _connection.OnBeforeInvoke(cancelArgs);

                // Wenn der Aufruf abgebrochen werden soll ...
                if (cancelArgs.Cancel)
                {
                    // Wenn keine Abbruchausnahme definiert ist ...
                    if (cancelArgs.CancelException == null)
                        // Standard-Abbruchausnahme erstellen
                        cancelArgs.CancelException = new InvokeCanceledException();

                    // InvokeCanceled-Ereignis feuern
                    _connection.OnInvokeCanceled(new InvokeCanceledEventArgs() { TrackingID = trackingID, CancelException = cancelArgs.CancelException });

                    // Abbruchausnahme werfen
                    throw cancelArgs.CancelException;
                }
                // Parametertypen ermitteln
                ParameterInfo[] paramDefs = methodCallMessage.MethodBase.GetParameters();

                try
                {
                    // Ggf. Delegaten-Parameter abfangen
                    object[] checkedArgs = InterceptDelegateParameters(methodCallMessage);

                    // Entfernten Methodenaufruf durchführen
                    returnValue = _remoteInvoker.Invoke(trackingID, _interfaceType.FullName, correlationSet, methodCallMessage.MethodName, paramDefs, checkedArgs);

                    // Ereignisargumente für AfterInvoke erstellen
                    AfterInvokeEventArgs afterInvokeArgs = new AfterInvokeEventArgs()
                    {
                        TrackingID = trackingID,
                        InterfaceName = _interfaceType.FullName,
                        DelegateCorrelationSet = correlationSet,
                        MethodName = methodCallMessage.MethodName,
                        Arguments = methodCallMessage.Args,
                        ReturnValue = returnValue
                    };
                    // AfterInvoke-Ereignis feuern
                    _connection.OnAfterInvoke(afterInvokeArgs);
                }
                catch (InvalidSessionException)
                {
                    // Wenn automatisches Anmelden bei abgelaufener Sitzung aktiviert ist ...
                    if (_autoLoginOnExpiredSession)
                    {
                        // Neu anmelden
                        _remoteInvoker.Logon(_sessionID, _autoLoginCredentials);

                        // Entfernten Methodenaufruf erneut versuchen                        
                        returnValue = _remoteInvoker.Invoke(trackingID, _interfaceType.FullName, correlationSet, methodCallMessage.MethodName, paramDefs, methodCallMessage.Args);
                    }
                }
                // Remoting-Antwortnachricht erstellen und zurückgeben
                return new ReturnMessage(returnValue, null, 0, methodCallMessage.LogicalCallContext, methodCallMessage);
            }
            catch (TargetInvocationException targetInvocationException)
            {
                // Aufrufausnahme als Remoting-Nachricht zurückgeben
                return new ReturnMessage(targetInvocationException, methodCallMessage);
            }
            catch (SocketException socketException)
            {
                // TCP-Sockelfehler als Remoting-Nachricht zurückgeben
                return new ReturnMessage(socketException, methodCallMessage);
            }
            catch (InvalidSessionException sessionException)
            {
                // Sitzungsfehler als Remoting-Nachricht zurückgeben
                return new ReturnMessage(sessionException, methodCallMessage);
            }
        }

        /// <summary>
        /// Ersetzt Delegaten-Parameter einer Remoting-Nachricht durch eine entsprechende Delegaten-Abfangvorrichtung.
        /// </summary>
        /// <param name="message">Remoting-Nachricht</param>
        /// <returns>argumentliste</returns>
        private object[] InterceptDelegateParameters(IMethodCallMessage message)
        { 
            // Argument-Array erzeugen
            object[] result = new object[message.ArgCount];

            // Alle Parameter durchlaufen
            for (int i = 0; i < message.ArgCount; i++)
            { 
                // Parameter abrufen
                object arg = message.Args[i];

                // Wenn der aktuelle Parameter ein Delegat ist ...
                if (typeof(Delegate).IsAssignableFrom(arg.GetType()))
                {
                    // Abfangvorrichtung erzeugen
                    DelegateInterceptor interceptor = new DelegateInterceptor()
                    {
                        ClientDelegate = arg
                    };
                    // Original-Parameter durch Abfangvorrichting in der Remoting-Nachricht ersetzen
                    result[i] = interceptor;
                }
                else
                    // 1:1
                    result[i] = arg;
            }
            // Arument-Array zurückgeben
            return result;
        }
    }
}

