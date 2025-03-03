using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace DecoderApp
{
	public class DecoderApp2
	{
		private TcpClient rfid_client;
		private TcpListener rr12_listener;
		
		private readonly string rr12_ip = "127.0.0.1";
		private readonly int rr12_port = 3601;

		private readonly string rfid_ip = "speedwayr-11-8e-a5";
		private readonly int rfid_port = 14150;

		public string deviceId = "T-21753";
		public float protocolVersion = 3.4F;

		// public byte[] protocol_messag_bytes;

		public DecoderApp2()
		{
			// Connect to RR12 Server
			rr12_listener = new TcpListener(IPAddress.Parse(rr12_ip), rr12_port);
			rr12_listener.Start();
			Console.WriteLine($"Listening for RR12 on {rr12_ip}:{rr12_port}");

			// var of connection to server
			TcpClient rr12_client = rr12_listener.AcceptTcpClient();
			NetworkStream rr12_stream = rr12_client.GetStream();
			// string protocol_message_string = $"SETPROTOCOL;{protocolVersion}\r\n"; // testing <CrLf> or \r\n
			// byte[] protocol_message_bytes = Encoding.UTF8.GetBytes(protocol_message_string);
			// rr12_stream.Write(protocol_message_bytes, 0, protocol_message_bytes.Length);

			string message_string = $"SETPROTOCOL;{protocolVersion}\r\nGETCONFIG;GENERAL;BOXNAME;Race Result Emulator;{deviceId}\r\n";			
			byte[] message_bytes = Encoding.UTF8.GetBytes(message_string);
			rr12_stream.Write(message_bytes, 0, message_bytes.Length);
			
			// byte[] buffer = new byte[1024];
			// int bytesRead = rr12_stream.Read(buffer, 0, buffer.Length);
			// string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
			// Console.WriteLine($"Received from RR12: {receivedMessage}");

			// string device_id_string = $"GETCONFIG;GENERAL;BOXNAME;Race Result Emulator;{deviceId}\r\n"; // testing <CrLf> or \r\n
			// byte[] device_id_bytes = Encoding.UTF8.GetBytes(device_id_string);
			// rr12_stream.Write(device_id_bytes, 0, device_id_bytes.Length);
			Console.WriteLine("Connected to RR12 Client!");

			// var declaration for rr12_client

			// Connect to the IMPINJ Reader
			rfid_client = new TcpClient();

			// var of connection to reader
			rfid_client.Connect(rfid_ip, rfid_port);
			NetworkStream rfid_stream = rfid_client.GetStream();
			Console.WriteLine($"Connected to IMPINJ Reader at {rfid_ip}:{rfid_port}");

			StartTransmission(rr12_client);
		}

		private void StartTransmission(TcpClient rr12_client)
		{
			// While loop
			while (true)
			{
				
				try
				{
					
					
					// Read from IMPINJ
					NetworkStream rfid_stream = rfid_client.GetStream();
					byte[] buffer = new byte[1024];
					int bytesRead = rfid_stream.Read(buffer, 0, buffer.Length);
					
					if (bytesRead > 0)
					{
						string receivedData = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
						Console.WriteLine($"Received from RFID Reader: {receivedData}");
					
					// Write to RR12
					NetworkStream rr12_stream = rr12_client.GetStream();
					rr12_stream.Write(buffer, 0, bytesRead);
					Console.WriteLine("Data forwarded to RR12");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
					break;
				}

				// Listen for way to exit loop
				if (false) // Change to an actual check later, false for now so that it never breaks connections.
				{
					// Close connection to Server
					rr12_listener.Stop();
					rr12_client.Close();

					// Close connection to Reader
					rfid_client.Close();

					// Break transmission loop
					break;
				}
			}
		}

		// public static void Main()
		// {
		// 	new DecoderApp2();
		// }
	}
}
