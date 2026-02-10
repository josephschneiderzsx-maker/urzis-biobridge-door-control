Imports System.ServiceProcess
Imports System.Net
Imports System.Threading
Imports System.Text
Imports System.IO
Imports System.Collections.Generic
Imports BioBridgeSDKDLL

Public Class Service1
    Inherits System.ServiceProcess.ServiceBase

#Region " Component Designer generated code "

    Public Sub New()
        MyBase.New()

        ' This call is required by the Component Designer.
        InitializeComponent()

        ' Add any initialization after the InitializeComponent() call

    End Sub

    'UserService overrides dispose to clean up the component list.
    Protected Overloads Overrides Sub Dispose(ByVal disposing As Boolean)
        If disposing Then
            If Not (components Is Nothing) Then
                components.Dispose()
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' The main entry point for the process
    <STAThread()> _
    Shared Sub Main()
        Dim ServicesToRun() As System.ServiceProcess.ServiceBase

        ' More than one NT Service may run within the same process. To add
        ' another service to this process, change the following line to
        ' create a second service object. For example,
        '
        '   ServicesToRun = New System.ServiceProcess.ServiceBase () {New Service1, New MySecondUserService}
        '
        ServicesToRun = New System.ServiceProcess.ServiceBase() {New Service1}

        System.ServiceProcess.ServiceBase.Run(ServicesToRun)

    End Sub

    'Required by the Component Designer
    Private components As System.ComponentModel.IContainer

    ' NOTE: The following procedure is required by the Component Designer
    ' It can be modified using the Component Designer.
    ' Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()> Private Sub InitializeComponent()
        '
        'Service1
        '
        Me.ServiceName = "UDM"

    End Sub

#End Region

    ' Variables de classe pour le serveur HTTP et SDK
    Private httpListener As HttpListener
    Private httpThread As Thread
    ' Utiliser BioBridgeSDKDLLv3.dll (assembly .NET) avec Interop.zkemkeeper.dll
    Private axBioBridgeSDK1 As BioBridgeSDKDLL.BioBridgeSDKClass
    Private isRunning As Boolean = False
    Private Const HTTP_PORT As Integer = 8080
    Private Const DEFAULT_TERMINAL_IP As String = "192.168.40.10"
    Private Const DEFAULT_TERMINAL_PORT As Integer = 4370

    ' État de la porte et connexion
    Private currentConnectedIP As String = ""
    Private doorStatus As String = "Unknown"
    Private lastDoorEvent As DateTime = DateTime.MinValue
    Private doorEventsHistory As New List(Of DoorEvent)
    Private doorStatusLock As New Object()

    ' Structure pour stocker les événements de porte
    Private Structure DoorEvent
        Dim EventType As Integer
        Dim EventTime As DateTime
        Dim Description As String
    End Structure

    Protected Overrides Sub OnStart(ByVal args() As String)
        Try
            ' Démarrer le serveur HTTP en premier (peut fonctionner même sans BioBridge)
            isRunning = True
            httpListener = New HttpListener()
            ' Ecouter sur toutes les interfaces (localhost + IP reseau)
            httpListener.Prefixes.Add("http://+:" & HTTP_PORT & "/")
            httpListener.Start()

            httpThread = New Thread(AddressOf StartHttpServer)
            httpThread.IsBackground = True
            httpThread.Start()

            CreateLog("HTTP Server started on port " & HTTP_PORT)
            CreateLog("Service ready - Endpoints: /open, /close, /status")

            ' Essayer d'initialiser la connexion BioBridge
            Try
                axBioBridgeSDK1 = New BioBridgeSDKDLL.BioBridgeSDKClass()
                CreateLog("BioBridge SDK instance created successfully")

                ' Connecter l'événement OnDoor
                Try
                    AddHandler axBioBridgeSDK1.OnDoor, AddressOf OnDoorEvent
                    AddHandler axBioBridgeSDK1.OnConnected, AddressOf OnConnectedEvent
                    AddHandler axBioBridgeSDK1.OnDisConnected, AddressOf OnDisConnectedEvent
                    CreateLog("Event handlers attached (OnDoor, OnConnected, OnDisConnected)")
                Catch ex As Exception
                    CreateLog("Warning: Could not attach event handlers. Error: " & ex.Message)
                End Try

                ' Tenter la connexion au terminal
                CreateLog("Connecting to terminal at " & DEFAULT_TERMINAL_IP & ":" & DEFAULT_TERMINAL_PORT & "...")
                If axBioBridgeSDK1.Connect_TCPIP("", 1, DEFAULT_TERMINAL_IP, DEFAULT_TERMINAL_PORT, 0) = 0 Then
                    currentConnectedIP = DEFAULT_TERMINAL_IP
                    Dim sFw As String = ""
                    If axBioBridgeSDK1.GetFirmwareVersion(sFw) = 0 Then
                        CreateLog("BioBridge Connected. Firmware: " & sFw)
                        doorStatus = "Connected"
                    End If
                Else
                    CreateLog("Failed to connect to BioBridge terminal at " & DEFAULT_TERMINAL_IP)
                    doorStatus = "Disconnected"
                End If
            Catch comEx As System.Runtime.InteropServices.COMException
                CreateLog("WARNING: BioBridge SDK COM error. Error: " & comEx.Message)
                CreateLog("The HTTP server is running, but BioBridge functions will not work.")
                doorStatus = "SDK Not Registered"
            Catch ex As Exception
                CreateLog("Warning: Could not initialize BioBridge SDK. Error: " & ex.GetType().Name & ": " & ex.Message)
                If ex.InnerException IsNot Nothing Then
                    CreateLog("Inner exception: " & ex.InnerException.Message)
                End If
                CreateLog("The HTTP server is running, but BioBridge functions may not work.")
                doorStatus = "Initialization Failed"
            End Try

        Catch ex As Exception
            CreateLog("Error in OnStart: " & ex.ToString())
            ' Même en cas d'erreur, essayer de démarrer le serveur HTTP
            Try
                If httpListener Is Nothing Then
                    isRunning = True
                    httpListener = New HttpListener()
                    httpListener.Prefixes.Add("http://localhost:" & HTTP_PORT & "/")
                    httpListener.Start()
                    httpThread = New Thread(AddressOf StartHttpServer)
                    httpThread.IsBackground = True
                    httpThread.Start()
                    CreateLog("HTTP Server started despite initialization errors")
                End If
            Catch httpEx As Exception
                CreateLog("CRITICAL: Could not start HTTP server. Error: " & httpEx.Message)
            End Try
        End Try
    End Sub

    Protected Overrides Sub OnStop()
        Try
            isRunning = False

            ' Arrêter le serveur HTTP
            If httpListener IsNot Nothing Then
                httpListener.Stop()
                httpListener.Close()
            End If

            If httpThread IsNot Nothing AndAlso httpThread.IsAlive Then
                httpThread.Join(2000) ' Attendre max 2 secondes
            End If

            ' Déconnecter le SDK
            If axBioBridgeSDK1 IsNot Nothing Then
                Try
                    axBioBridgeSDK1.Disconnect()
                Catch ex As Exception
                    CreateLog("Warning during SDK disconnect: " & ex.Message)
                End Try
                axBioBridgeSDK1 = Nothing
            End If

            CreateLog("Service stopped")

        Catch ex As Exception
            CreateLog("Error in OnStop: " & ex.ToString())
        End Try
    End Sub

    Private Sub StartHttpServer()
        While isRunning
            Try
                Dim context As HttpListenerContext = httpListener.GetContext()
                ThreadPool.QueueUserWorkItem(AddressOf HandleRequest, context)
            Catch ex As HttpListenerException
                ' Normal quand on arrête le listener
                If isRunning Then
                    CreateLog("HTTP Listener error: " & ex.Message)
                End If
            Catch ex As Exception
                If isRunning Then
                    CreateLog("HTTP Server error: " & ex.ToString())
                End If
            End Try
        End While
    End Sub

    Private Sub AddCorsHeaders(response As HttpListenerResponse)
        response.Headers.Add("Access-Control-Allow-Origin", "*")
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type")
    End Sub

    Private Sub HandleRequest(state As Object)
        Dim context As HttpListenerContext = CType(state, HttpListenerContext)
        Dim request As HttpListenerRequest = context.Request
        Dim response As HttpListenerResponse = context.Response

        Try
            AddCorsHeaders(response)
            Dim path As String = request.Url.AbsolutePath.ToLower()

            ' Repondre au preflight CORS
            If request.HttpMethod = "OPTIONS" Then
                response.StatusCode = 204
            ElseIf request.HttpMethod = "POST" Then
                If path = "/open" Then
                    HandleOpenRequest(context)
                ElseIf path = "/close" Then
                    HandleCloseRequest(context)
                ElseIf path = "/status" Then
                    HandleStatusRequest(context)
                Else
                    SendNotFound(response)
                End If
            ElseIf request.HttpMethod = "GET" Then
                If path = "/status" Then
                    HandleStatusRequest(context)
                Else
                    SendNotFound(response)
                End If
            Else
                SendNotFound(response)
            End If

        Catch ex As Exception
            CreateLog("Error handling request: " & ex.ToString())
            SendError(response, ex.Message)
        Finally
            response.Close()
        End Try
    End Sub

    Private Sub HandleOpenRequest(context As HttpListenerContext)
        Dim request As HttpListenerRequest = context.Request
        Dim response As HttpListenerResponse = context.Response

        Try
            ' Lire le body JSON
            Dim reader As New StreamReader(request.InputStream, request.ContentEncoding)
            Dim jsonBody As String = reader.ReadToEnd()

            ' Parser simple du JSON
            Dim terminalIP As String = DEFAULT_TERMINAL_IP
            Dim delay As Integer = 1000 ' 1 seconde par défaut

            If jsonBody.Contains("terminalIP") Then
                Dim ipStart As Integer = jsonBody.IndexOf("""terminalIP"":""") + 14
                Dim ipEnd As Integer = jsonBody.IndexOf("""", ipStart)
                If ipEnd > ipStart Then
                    terminalIP = jsonBody.Substring(ipStart, ipEnd - ipStart)
                End If
            End If

            If jsonBody.Contains("delay") Then
                Dim delayStart As Integer = jsonBody.IndexOf("""delay"":") + 8
                Dim delayEnd As Integer = jsonBody.IndexOf("}", delayStart)
                If delayEnd = -1 Then delayEnd = jsonBody.IndexOf(",", delayStart)
                If delayEnd = -1 Then delayEnd = jsonBody.Length
                Dim delayStr As String = jsonBody.Substring(delayStart, delayEnd - delayStart).Trim()
                Integer.TryParse(delayStr, delay)
            End If

            ' Valider les paramètres
            If delay <= 0 Then
                delay = 1000 ' Valeur par défaut
            End If

            ' Ouvrir la porte
            Dim result As Boolean = OpenDoor(terminalIP, delay)

            ' Préparer la réponse JSON
            Dim jsonResponse As String
            If result Then
                response.StatusCode = 200
                jsonResponse = "{""success"":true,""message"":""Door opened successfully"",""delay"":" & delay & ",""status"":""open""}"
                CreateLog("Door opened via HTTP - IP: " & terminalIP & ", Delay: " & delay & "ms")
            Else
                response.StatusCode = 500
                jsonResponse = "{""success"":false,""message"":""Failed to open door""}"
                CreateLog("Failed to open door via HTTP - IP: " & terminalIP)
            End If

            SendJsonResponse(response, jsonResponse)

        Catch ex As Exception
            CreateLog("Error in HandleOpenRequest: " & ex.ToString())
            SendError(response, ex.Message)
        End Try
    End Sub

    Private Sub HandleCloseRequest(context As HttpListenerContext)
        Dim request As HttpListenerRequest = context.Request
        Dim response As HttpListenerResponse = context.Response

        Try
            SyncLock doorStatusLock
                Dim jsonResponse As String
                If doorStatus = "Closed" OrElse doorStatus = "closed" Then
                    response.StatusCode = 200
                    jsonResponse = "{""success"":true,""message"":""Door is already closed"",""status"":""closed""}"
                Else
                    response.StatusCode = 200
                    jsonResponse = "{""success"":true,""message"":""Door will close automatically after unlock delay"",""status"":""closing""}"
                End If
                SendJsonResponse(response, jsonResponse)
            End SyncLock

            CreateLog("Door close status checked via HTTP")

        Catch ex As Exception
            CreateLog("Error in HandleCloseRequest: " & ex.ToString())
            SendError(response, ex.Message)
        End Try
    End Sub

    Private Sub HandleStatusRequest(context As HttpListenerContext)
        Dim response As HttpListenerResponse = context.Response

        Try
            SyncLock doorStatusLock
                Dim lastEventDesc As String = "None"
                If doorEventsHistory.Count > 0 Then
                    Dim lastEvt As DoorEvent = doorEventsHistory(doorEventsHistory.Count - 1)
                    lastEventDesc = lastEvt.Description & " at " & lastEvt.EventTime.ToString("yyyy-MM-dd HH:mm:ss")
                End If

                Dim connectedStr As String = If(axBioBridgeSDK1 IsNot Nothing, "true", "false")
                Dim jsonResponse As String = "{""status"":""" & doorStatus & """,""lastEvent"":""" & lastEventDesc & """,""eventsCount"":" & doorEventsHistory.Count & ",""connected"":" & connectedStr & "}"
                SendJsonResponse(response, jsonResponse)
            End SyncLock

        Catch ex As Exception
            CreateLog("Error in HandleStatusRequest: " & ex.ToString())
            SendError(response, ex.Message)
        End Try
    End Sub

    Private Function OpenDoor(terminalIP As String, delay As Integer) As Boolean
        Try
            If axBioBridgeSDK1 Is Nothing Then
                CreateLog("ERROR: BioBridge SDK not initialized")
                Return False
            End If

            ' Déconnecter si on change de terminal
            If currentConnectedIP <> "" AndAlso currentConnectedIP <> terminalIP Then
                CreateLog("Switching terminal: disconnecting from " & currentConnectedIP & " to connect to " & terminalIP)
                Try
                    axBioBridgeSDK1.Disconnect()
                Catch ex As Exception
                    CreateLog("Warning during disconnect: " & ex.Message)
                End Try
                currentConnectedIP = ""
                Thread.Sleep(500) ' Laisser le SDK se stabiliser après déconnexion
            End If

            ' Connecter au terminal
            Dim connectResult As Integer = -1
            Try
                connectResult = axBioBridgeSDK1.Connect_TCPIP("", 1, terminalIP, DEFAULT_TERMINAL_PORT, 0)
            Catch connEx As Exception
                CreateLog("Exception during Connect_TCPIP to " & terminalIP & ": " & connEx.Message)
                currentConnectedIP = ""
                Return False
            End Try

            If connectResult = 0 Then
                currentConnectedIP = terminalIP
                Dim result As Integer = axBioBridgeSDK1.UnlockDoor(delay)

                If result = 0 Then
                    SyncLock doorStatusLock
                        doorStatus = "Open"
                        lastDoorEvent = DateTime.Now
                        Dim doorEvt As New DoorEvent With {
                            .EventType = 1,
                            .EventTime = DateTime.Now,
                            .Description = "Door opened (UnlockDoor called)"
                        }
                        doorEventsHistory.Add(doorEvt)
                        If doorEventsHistory.Count > 100 Then
                            doorEventsHistory.RemoveAt(0)
                        End If
                    End SyncLock

                    CreateLog("Door opened successfully - IP: " & terminalIP & ", Delay: " & delay & "ms")
                    Return True
                Else
                    CreateLog("Failed to open door - IP: " & terminalIP & ", Error code: " & result)
                    Return False
                End If
            Else
                CreateLog("Failed to connect to terminal: " & terminalIP & " (result=" & connectResult & ")")
                currentConnectedIP = ""
                SyncLock doorStatusLock
                    doorStatus = "Disconnected"
                End SyncLock
                Return False
            End If

        Catch ex As Exception
            CreateLog("Exception in OpenDoor: " & ex.ToString())
            Return False
        End Try
    End Function

    ' Événement OnDoor du SDK BioBridge
    ' Types d'événements possibles :
    ' - Type 1 : La porte est ouverte soudainement
    ' - Type 4 : La porte n'est pas bien fermée
    ' - Type 5 : La porte est fermée
    ' - Type 53 : La porte est ouverte en appuyant sur le bouton Off-Exit
    Private Sub OnDoorEvent(eventType As Integer)
        Try
            SyncLock doorStatusLock
                Dim eventDesc As String = ""
                Select Case eventType
                    Case 1
                        eventDesc = "Door opened suddenly"
                        doorStatus = "Open"
                    Case 4
                        eventDesc = "Door not closed well"
                        doorStatus = "Ajar"
                    Case 5
                        eventDesc = "Door closed"
                        doorStatus = "Closed"
                    Case 53
                        eventDesc = "Door opened by Off-Exit button"
                        doorStatus = "Open"
                    Case Else
                        eventDesc = "Door event type " & eventType
                End Select

                lastDoorEvent = DateTime.Now
                Dim doorEvt As New DoorEvent With {
                    .EventType = eventType,
                    .EventTime = DateTime.Now,
                    .Description = eventDesc
                }
                doorEventsHistory.Add(doorEvt)

                If doorEventsHistory.Count > 100 Then
                    doorEventsHistory.RemoveAt(0)
                End If

                CreateLog("Door Event: " & eventDesc & " (Type: " & eventType & ")")
            End SyncLock

        Catch ex As Exception
            CreateLog("Error in OnDoorEvent: " & ex.ToString())
        End Try
    End Sub

    Private Sub OnConnectedEvent()
        CreateLog("BioBridge Connected event received")
        SyncLock doorStatusLock
            doorStatus = "Connected"
        End SyncLock
    End Sub

    Private Sub OnDisConnectedEvent()
        CreateLog("BioBridge Disconnected event received")
        SyncLock doorStatusLock
            doorStatus = "Disconnected"
        End SyncLock
    End Sub

    Private Sub SendJsonResponse(response As HttpListenerResponse, jsonResponse As String)
        response.ContentType = "application/json"
        response.ContentEncoding = Encoding.UTF8
        Dim buffer As Byte() = Encoding.UTF8.GetBytes(jsonResponse)
        response.ContentLength64 = buffer.Length
        response.OutputStream.Write(buffer, 0, buffer.Length)
    End Sub

    Private Sub SendError(response As HttpListenerResponse, errorMessage As String)
        response.StatusCode = 500
        Dim errorResponse As String = "{""success"":false,""error"":""" & errorMessage & """}"
        SendJsonResponse(response, errorResponse)
    End Sub

    Private Sub SendNotFound(response As HttpListenerResponse)
        response.StatusCode = 404
        response.StatusDescription = "Not Found"
        Dim buffer As Byte() = Encoding.UTF8.GetBytes("{""error"":""Endpoint not found""}")
        response.ContentLength64 = buffer.Length
        response.OutputStream.Write(buffer, 0, buffer.Length)
    End Sub

    Protected Sub CreateLog(ByVal sMsg As String)
        Dim sSource As String
        Dim sLog As String
        Dim sEvent As String
        Dim sMachine As String

        sSource = "UDM"
        sLog = "Application"
        sEvent = "Door Control Event"
        sMachine = "."
        Dim eSource As EventSourceCreationData = New EventSourceCreationData(sSource, sLog)

        If Not EventLog.SourceExists(sSource, sMachine) Then
            EventLog.CreateEventSource(eSource)
        End If

        Dim ELog As New EventLog(sLog, sMachine, sSource)
        ELog.WriteEntry(sMsg)
    End Sub

End Class
