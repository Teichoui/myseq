using System;

using SpeechLib;

using System.IO;

using System.Drawing;

using System.Threading;

using System.Collections;

using System.Diagnostics;

using System.Windows.Forms;

using System.ComponentModel;

using WeifenLuo.WinFormsUI;

using System.Text.RegularExpressions;

using System.Runtime.InteropServices;



// Class Files

using SocketSystem;

using Structures;



namespace myseq 

{   

    public enum RequestType 

    {

        //Bit Flags determining what data to send to the client

        ZONE = 0x00000001,

        PLAYER = 0x00000002,

        TARGET = 0x00000004,

        MOBS = 0x00000008,

        GROUND_ITEMS = 0x00000010,

        GET_PROCESSINFO = 0x00000020,

        SET_PROCESSINFO = 0x00000040,

        WORLD = 0x00000080

    }



    public class EQCommunications

    {

        // Puts the server's responses back together when they arrive in pieces.

        private readonly PacketFramer framer = new PacketFramer();

        public bool RequestPending = false;

        public CSocketClient pSocketClient = null;

        private bool update_hidden = false;

        private bool mbGetProcessInfo = false;

        public int newProcessID = 0;

        bool send_process = false;

        

        private EQData eq;

        private frmMain f1; // TODO: get rid of this

        public void UpdateHidden()
        {
            update_hidden = true;
        }

        public EQCommunications(EQData eq,frmMain f1)

        {

            this.eq = eq;

            this.f1 = f1;

        }

        

        public void StopListening() 

        {

            try 

            {

                RequestPending = false;

                framer.Reset();



                if (pSocketClient != null)

                    pSocketClient.Dispose();



                pSocketClient = null;

            }

            catch (Exception pException) {LogLib.WriteLine("Error with StopListening(): " + pException.Message);}

        }

        

        public bool ConnectToServer(string ServerAddress, int ServerPort, bool errMsg = true) 

        {

            try {

                if (pSocketClient != null) {

                    Thread.Sleep(2000);

                    pSocketClient.Dispose();

                    pSocketClient = null;

                }



                // Instantiate a CSocketClient object

                pSocketClient = new CSocketClient(100000, null, 

                    new CSocketClient.MESSAGE_HANDLER(MessageHandlerClient),

                    new CSocketClient.CLOSE_HANDLER(CloseHandler), 

                    new CSocketClient.ERROR_HANDLER(ErrorHandler));



                // Establish a connection to the server
                mbGetProcessInfo = true;
                pSocketClient.Connect(ServerAddress, ServerPort);


                return true;

            }

            catch (Exception pException) 

            {   

                string msg = String.Format("Could not connect to the server: {0}",pException.Message);

                LogLib.WriteLine(msg);

                if (errMsg)
                    MessageBox.Show(msg + "\r\nTry selecting a different server!","Server Connection Error",MessageBoxButtons.OK,MessageBoxIcon.Error);

                return false;

            }

        }



        //********************************************************************

        /// <summary> Called when a message is extracted from the socket </summary>

        /// <param name="pSocket"> The SocketClient object the message came from </param>

        /// <param name="iNumberOfBytes"> The number of bytes in the RawBuffer inside the SocketClient </param>

        public void MessageHandlerClient(CSocketClient pSocket, Int32 iNumberOfBytes) 

        {

            // Process the packet

            try {ProcessPacket(pSocket.GetRawBuffer, iNumberOfBytes);}

            catch (Exception pException) {LogLib.WriteLine("Error with ProcessPacket: " + pException.Message);}

        }



        //********************************************************************

        /// <summary> Called when a socket connection is closed </summary>

        /// <param name="pSocket"> The SocketClient object the message came from </param>

        public void CloseHandler(CSocketClient pSocket) 

        {

            try {

                if (f1 != null)

                    f1.StopListening();

                else

                    StopListening();

            }

            catch (Exception pException) {LogLib.WriteLine("Error with CloseHandler(): " + pException.Message);}

        }

        //********************************************************************

        /// <summary> Called when a socket error occurs </summary>

        /// <param name="pSocket"> The SocketClient object the message came from </param>

        /// <param name="pException"> The reason for the error </param>

        public void ErrorHandler(CSocketClient pSocket, Exception pException) 

        {

            LogLib.WriteLine("Error with ErrorHandler(): " + pException.Message);

            MessageBox.Show (pException.Message);

        }



        private void SendData(byte[] data) 

        {

            pSocketClient.Send(data);

        }           

        

        public void Tick()        

        {

            int Request;

            

            try 

            {

                if (!RequestPending) 

                {

                    

                    if (newProcessID > 0 && !mbGetProcessInfo)
                    {
                        if (send_process == false)
                        {
                            // We have a request to change the process
                            //byte[] requestdata = new byte[8];

                            //byte[] request = BitConverter.GetBytes((int)(RequestType.SET_PROCESSINFO ));
                            Request = (int)(RequestType.SET_PROCESSINFO);
                            //Array.Copy(request, 0, requestdata, 0, 4);

                            //byte[] proc = BitConverter.GetBytes((uint) newProcessID);

                            //Array.Copy(proc, 0, requestdata, 4, 4);
                            SendData(BitConverter.GetBytes(Request));
                            send_process = true;
                        }
                        else
                        {
                            SendData(BitConverter.GetBytes(newProcessID));
                            send_process = false;
                            //SendData(requestdata);

                            newProcessID = 0;

                            mbGetProcessInfo = true;
                        }
                    }
                    else
                    {
                        RequestPending = true;
                        Request = (int)(RequestType.ZONE | RequestType.PLAYER | RequestType.TARGET | RequestType.MOBS | RequestType.GROUND_ITEMS | RequestType.WORLD);



                        if (mbGetProcessInfo && newProcessID == 0)
                        {

                            mbGetProcessInfo = false;

                            Request = Request | (int)RequestType.GET_PROCESSINFO;

                        }



                        SendData(BitConverter.GetBytes(Request));
                    }

                }

            }

            catch (Exception ex) {LogLib.WriteLine("Error in timPackets_Tick: ", ex);}

            

        }

        

        public void CharRefresh()

        {

            if (pSocketClient != null) 

            {

                //if (RequestPending) 

                //{

                    mbGetProcessInfo = true;

                //}

                //else

                //{

                //    RequestPending = true;

                //    int request = (int)RequestType.GET_PROCESSINFO;

                //    SendData(BitConverter.GetBytes(request));

                //}        

            }

        }



        public void SwitchCharacter(ProcessInfo PI)

        {
            
            if (PI != null && PI.ProcessID > 0)
                newProcessID = PI.ProcessID;

            //byte[] requestdata = new byte[8];

            //byte[] request = BitConverter.GetBytes((int)(RequestType.SET_PROCESSINFO | RequestType.ZONE | RequestType.PLAYER | RequestType.TARGET | RequestType.MOBS | RequestType.GROUND_ITEMS | RequestType.WORLD));

            //Array.Copy(request, 0, requestdata, 0, 4);



            //byte[] proc = BitConverter.GetBytes(PI.ProcessID);

            //Array.Copy(proc, 0, requestdata, 4, 4);

            //SendData(requestdata);

        }

        public bool CanSwitchChars()
        {
            return ((newProcessID == 0) && (mbGetProcessInfo == false));
        }

        public void ProcessPacket(byte[] packet, int bytes)
        {
            bool complete = false;

            try
            {
                if (bytes > 0)
                {
                    eq.BeginProcessPacket();

                    // A read can end anywhere in a response, so the framer holds back a partial
                    // header or record until the rest arrives.
                    complete = framer.Feed(packet, bytes,
                        count => f1.StartNewPackets(),
                        (buffer, offset) =>
                        {
                            // One bad record must not lose the ones after it or throw the stream off.
                            try
                            {
                                SPAWNINFO si = new SPAWNINFO();
                                si.frombytes(buffer, offset);
                                f1.ProcessPacket(si, update_hidden);
                            }
                            catch (Exception ex) {LogLib.WriteLine("Error in ProcessPacket() processing a record: ", ex);}
                        });

                    f1.ProcessSpawnList();
                    f1.ProcessGroundItemList();
                    //f1.ProcessSpawnTimer();
                }
            }
            catch (Exception ex) {LogLib.WriteLine("Error in ProcessPacket(): ", ex);}

            if (complete)
            {
                // Finished proceessing the request
                RequestPending = false;
                if (update_hidden)
                    update_hidden = false;
                f1.checkMobs();
                f1.mapCon.Invalidate();
            }
        }

    }

}