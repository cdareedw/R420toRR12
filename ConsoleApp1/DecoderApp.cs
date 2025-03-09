
// see https://www.raceresult.com/en/support/kb?id=18-Device-Communication for protocol

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;

public class DecoderApp
{
    private readonly AppSettings _settings;
    private TcpListener _rr12Listener;
    private TcpClient _rr12Client;
    private TcpClient _rfidClient;

    private bool _isOperational;
    private string _lastRfidData;
    private double _ProtocolVersion;
    private string _deviceID;


    public DecoderApp(AppSettings settings)
    {
        _settings = settings;
        _ProtocolVersion = settings.ProtocolVersion;
        _deviceID = settings.deviceID;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Starting DecoderApp...");
        StartListeningForRr12();
        await ConnectToRfidStream();

        // Start status updater and continuous feeding tasks
        _ = Task.Run(() => StatusUpdater());
        _ = Task.Run(() => ContinuousDataFeed());
    }

    private void StartListeningForRr12()
    {
        _rr12Listener = new TcpListener(IPAddress.Parse(_settings.Rr12IpAddress), _settings.Rr12Port);
        _rr12Listener.Start();
        Console.WriteLine($"Listening for RR12 on {_settings.Rr12IpAddress}:{_settings.Rr12Port}");

        _ = Task.Run(async () =>
        {
            while (true)
            {
                _rr12Client = await _rr12Listener.AcceptTcpClientAsync();
                Console.WriteLine("RR12 connected.");
                await HandleRr12Client(_rr12Client);
            }
        });
    }

    private async Task HandleRr12Client(TcpClient client)
    {
        using var stream = client.GetStream();
        var buffer = new byte[1024];
        while (true)
        {
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                // If no bytes are read (stream might be closed or empty), continue to the next iteration.
                if (bytesRead == 0)
                {
                    Console.WriteLine("Connection closed or no data received.");
                    break;
                }
                string request = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();  // Use ASCII encoding for incoming data
                Console.WriteLine($"Received from RR12: {request}");

                
                string[] tokens = request.Split(";");
                switch( tokens[0]){

                       case "SETPROTOCOL" : 
                
                            // Protocol version handler
                            var match = Regex.Match(request, @"^SETPROTOCOL;<=([0-9.]+)$");
                            string requestedVersion = match.Groups[1].Value;

                            if (double.Parse(requestedVersion) >= _ProtocolVersion)
                            {
                                Console.WriteLine($"Replied: SETPROTOCOL;{_ProtocolVersion}\r\n");
                                await stream.WriteAsync(Encoding.ASCII.GetBytes($"SETPROTOCOL;{_ProtocolVersion}\r\n"));
                            }
                            else
                            {
                                Console.WriteLine("Responding: ERROR,Unsupported protocol version\r\n");
                                await stream.WriteAsync(Encoding.ASCII.GetBytes("ERROR,Unsupported protocol version\r\n"));
                            }
                            break;

                        case "GETCONFIG" :
                        
                            switch( request) {

                                    case "GETCONFIG;GENERAL;BOXNAME" :
                                        Console.WriteLine($"Responded: GETCONFIG;GENERAL;BOXNAME;Race Result Emulator;{_deviceID}\r\n");
                                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GETCONFIG;GENERAL;BOXNAME;Race Result Emulator;{_deviceID}\r\n"));
                                    break;
                                    case "GETCONFIG;UPLOAD;CUSTNO" :
                                        Console.WriteLine($"Responded: GETCONFIG;UPLOAD;CUSTNO;123456\r\n");
                                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GETCONFIG;UPLOAD;CUSTNO;123456\r\n"));
                                    break;
                                    case "GETCONFIG;DETECTION;DEADTIME":
                                        Console.WriteLine($"Responded: GETCONFIG;DETECTION;DEADTIME;500\r\n");
                                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GETCONFIG;DETECTION;DEADTIME;500\r\n"));
                                    break;
                                    case "GETCONFIG;DETECTION;REACTIONTIME" :
                                        Console.WriteLine($"Responded: GETCONFIG;DETECTION;REACTIONTIME;500\r\n");
                                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GETCONFIG;DETECTION;REACTIONTIME;500\r\n"));
                                    break;
                                    case "GETCONFIG;DETECTION;NOTIFICATION" :
                                        Console.WriteLine($"Responded: GETCONFIG;DETECTION;NOTIFICATION;BEEP\r\n");
                                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GETCONFIG;DETECTION;NOTIFICATION;BEEP\r\n"));
                                        break;
                                    default:
                                        Console.WriteLine("Missing response");
                                    break;
                            }
                            break;
                
                            case "GETFIRMWAREVERSION":
                                Console.WriteLine($"Responded: GETFIRMWAREVERSION;1.94\r\n");
                                await stream.WriteAsync(Encoding.ASCII.GetBytes($"GETFIRMWAREVERSION;1.94\r\n"));
                                break;
                
                            case "SETPUSHPASSINGS":

                                int setPush = Int32.Parse(tokens[1]);
                                int setHold = Int32.Parse(tokens[2]);
                                _isOperational = (setPush!=0);
                                
                           
                                Console.WriteLine($"Responded: SETPUSHPASSINGS;{setPush};{setHold}\r\n");
                                await stream.WriteAsync(Encoding.ASCII.GetBytes($"SETPUSHPASSINGS;{setPush};{setHold}\r\n"));
                                break;

                            case "GETACTIVESTATUS":
                                string activestatus = $"GETACTIVESTATUS;1;0;1;1;100;1;1;1;1;100;12;1;1\r\n";
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(activestatus));
                                Console.WriteLine($"Sent status: {activestatus}");
                                break;
                            case "GETSTATUS":
                                string status = this.GetStatus() + "\r\n";
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(status));
                                Console.WriteLine($"Sent status: {status}");
                                break;
                            case "PASSINGS":
                                await stream.WriteAsync(Encoding.ASCII.GetBytes("PASSINGS;1\r\n"));
                                Console.WriteLine("Sent PASSING;1");
                                break;

                            default :
                                Console.WriteLine("Unknown command");
                                break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling RR12 client: {ex.Message}");
            }
            // Ensure the loop continues processing further requests
            await Task.Delay(1); // Prevent CPU spinning; small delay ensures continuous checking for new data
        }
    }

    public string GetPassing( string passingNo, string Bib )
    {
        DateTime currentTime = DateTime.UtcNow.AddHours(11);

        string[] passing ={
                "#P",
                passingNo, //Passings record number, starting at 1 for the first passing.
                Bib, //Bib number of the transponder. (Since protocol version 1.2: Or transponder code in case of multi use tags or Active transponders.)
               "0000-00-00", // <Date>; If GPS is used, date of the detection, otherwise 0000-00-00 or 0000-00-01 after 24 hours and so on.
               $"{currentTime:HH:mm:ss.fff}", //<Time>; Time of the detection, format: hh:mm:ss.kkk
                "0", //	ID of the bib set. The combination of <Bib> and <EventID> is unique for all RACE RESULT Bib Transponders ever produced. In case of multi use tags <EventID> is 0 or empty.
                "1", //Number of times the tag was detected.
                "25", //	Maximum RSSI value found while determining <Time>. (Maximum value for passive detection is 25-1 = 31; for RSSI of active detection see here).
                "", //	This field is only used for internal purposes and is optional.
                "1", //1 if this passing is from an active transponder
                "", // [<Channel>] Channel ID (1..8) 
                "",//  [<LoopID>] Loop ID (1..8) 
                "",// [<LoopOnly>]	1, if this detection was generated in Store Mode.
                "",// [<WakeupCounter>]	Overall wakeup counter of the transponder (new transponders start at 10000).
                "",// [<Battery>]	Battery level in Volts.
                "",// [<Temperature>] Temperature in degrees Celsius.  
                "",// [<InternalActiveData>] Data transmission details. One byte. Lowest three bits: channel busy counter (number of times, the passing could not be transmitted because the transponder could not access the channel). Next three bits: no ACK counter (number of times the passing was transmitted, but no acknowledgement was received.) Seventh bit: 1, if this passing could not be transmitted at all in a previous attempt (="stored passing", old passing), 0 otherwise. Highest bit: 1, if the transponder woke up from deep sleep because of the passing, 0 otherwise.
                "Race Result Emulator", // <BoxName> Name of the decoder. Defaults to the Device ID.
                "1",//<FileNumber>	File number of the file to which this passing belongs.
                "",// [<MaxRSSIAntenna>]	Antenna number (1..8) which meassured the highest RSSI value. Empty for active.
                $"{_deviceID}", // <BoxId> Device ID
        };
        return String.Join(";",passing);
    }

    public string GetStatus()
    {
        DateTime currentTime = DateTime.UtcNow.AddHours(11);
        string[] status ={
                "GETSTATUS",
                $"{currentTime:yyyy-MM-dd}", // <Date>;
                $"{currentTime:HH:mm:ss.fff}", //<Time>;
                "1",   //<HasPower>; 	1 if the decoder has power via its Schuko AC power socket, 0 otherwise
                "00011001",    //<Antennas>; 	A sequence of 8 antenna status indicators of the UHF unit. Each one is 1 if an antenna is connected, 0 otherwise. EC. 00011001
                "1",    //<IsInOperationMode>, 	1 if the decoder is in Operation Mode, 0 otherwise
                "1",    //<FileNumber>, 	Current file number that is used to save the passings
                "1",    //<GPSHasFix>, 1 if device has a GPS satellite fix, 0 otherwise
                "49.721,8.254939",   //<Latitude>,<Longitude>, If the device has a GPS fix, the latitude of the device.
                "1",    //<ReaderIsHealthy>, 1, if the internal UHF module is in normal condition, 0 otherwise
                "100",    //<BatteryCharge>, Battery charge in percent
                "10",    //<BoardTemperature>, 	Main board temperature in degrees Celsius. Only valid if no Active Extension is connected. 
                "10",    //<ReaderTemperature>, 	Temperature of the UHF module in degrees Celsius. Only valid when there is not an Active Extension connected to the Feature Port.
                "0",    //<UHFFrequency>, 	Currently selected frequency number. EU: 0 if frequency is set to Auto, 1=A, 2=B; JP: 0-3; All other regions: 0
                "0",    //<ActiveExtConnected>, 1, if an Active Extension is connected
                "",    //[<Channel>]; 	Selected channel (1 -> 8)
                "",     // [<LoopID>];  Selected loop ID (1st -> 8)
                "",     // [<LoopPower>]; Selected loop power (0% -> 100%)
                "",     // [<LoopConnected>]; 1, if a loop is connected
                "",     // [<LoopUnderPower>]; 1, if the loop has reached its "Loop Limit"
                "0",     // <TimeIsRunning>; 	0 if there is a static time and the decoder is not in timing mode. 1 otherwise.
                "1",     // <TimeSource>; One of: 0:    Time was set manually 1:    Time was set by GPS 2:    Time was set by GPS, but time is only estimated due to bad reception.
                "1",     // <ScheduledStandbyEnabled>; 1, if scheduled standby is enabled.
                "1",     // <IsInStandby>; 1, if decoder is currently in standby.
                "0x0",     // <ErrorFlags>; 	
                        //   0, if everything is OK. Otherwise bitmask with the following meaning:
                        //    Bit	Meaning
                        //    1	UHF module reports an error
                        //    16	Active loop error
                        //    32	Active loop limit
                        //    64	Active connection solved
                        //    256	GPS time sync error
                        //    512	GPS communication error warning
                        //    1024	Active time sync error
                "",     // [<External12Volt>]
        };

        return String.Join(";",status);
    }

    private async Task ConnectToRfidStream()
    {
        while (true)
        {
            try
            {
                _rfidClient = new TcpClient();
                await _rfidClient.ConnectAsync(_settings.RfidStreamIpAddress, _settings.RfidStreamPort);
                Console.WriteLine("Connected to RFID stream.");
                await HandleRfidStream(_rfidClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to RFID stream: {ex.Message}. Retrying...");
                await Task.Delay(5000);
            }
        }
    }

    private async Task HandleRfidStream(TcpClient client)
    {
        using var stream = client.GetStream();
        var buffer = new byte[1024];
        var passing_count=0;
        while (true)
        {
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                _lastRfidData = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();  // Ensure RFID data is processed in ASCII
                Console.WriteLine($"Received RFID data: {_lastRfidData}");

                if (_isOperational && _rr12Client != null && _rr12Client.Connected)
                {
                    var rr12Stream = _rr12Client.GetStream();

                    var passing = this.GetPassing($"{passing_count}",$"{_lastRfidData}") + "\r\n"; 
                    passing_count += 1;
                    await rr12Stream.WriteAsync(Encoding.ASCII.GetBytes(passing));  // Send data to RR12 in ASCII
                    Console.WriteLine($"Forwarded RFID data to RR12: {passing}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling RFID stream: {ex.Message}");
                //break;
            }
        }
    }

    private async Task StatusUpdater()
    {
        while (true)
        {
            if (_isOperational)
            {
                Console.WriteLine("System status: Operational");
            }
            else
            {
                Console.WriteLine("System status: Idle");
            }
            await Task.Delay(_settings.StatusUpdateInterval);
        }
    }

    private async Task ContinuousDataFeed()
    {
        while (true)
        {
            if (_isOperational && _rr12Client != null && _rr12Client.Connected)
            {
                var rr12Stream = _rr12Client.GetStream();
                if (!string.IsNullOrEmpty(_lastRfidData))
                {
                    await rr12Stream.WriteAsync(Encoding.ASCII.GetBytes(_lastRfidData));  // Use ASCII encoding for continuous data
                    Console.WriteLine($"Continuously sent RFID data: {_lastRfidData}");
                }
                else
                {
                    Console.WriteLine("No RFID data to send.");
                }
            }
            await Task.Delay(1000); // Delay between continuous data sends (1 second)
        }
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        // Load settings from JSON file
        var settings = LoadSettings();
        var decoderApp = new DecoderApp(settings);

        await decoderApp.RunAsync();
    }

    private static AppSettings LoadSettings()
    {
        var settingsJson = File.ReadAllText("settings.json");
        return JsonSerializer.Deserialize<AppSettings>(settingsJson);
    }
}