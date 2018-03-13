using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBSChatBot
{
    public class OBSWebsocketHandler
    {
        public readonly OBSWebsocket Obs;

        public OBSWebsocketHandler(string ip, string password)
        {
            Obs = new OBSWebsocket();

            Obs.Connected += onConnect;
            Obs.Disconnected += onDisconnect;

            Obs.SceneChanged += onSceneChange;
            Obs.SceneCollectionChanged += onSceneColChange;
            Obs.ProfileChanged += onProfileChange;
            Obs.TransitionChanged += onTransitionChange;
            Obs.TransitionDurationChanged += onTransitionDurationChange;

            Obs.StreamingStateChanged += onStreamingStateChange;
            Obs.RecordingStateChanged += onRecordingStateChange;

            Obs.StreamStatus += onStreamData;

            Obs.Connect(ip, password);
        }

        private void onConnect(object sender, EventArgs e)
        {

            Console.WriteLine("Web Socket Connect");

            var streamStatus = Obs.GetStreamingStatus();
            if (streamStatus.IsStreaming)
            {
                onStreamingStateChange(Obs, OutputState.Started);
            }
            else
            {
                onStreamingStateChange(Obs, OutputState.Stopped);
            }
            if (streamStatus.IsRecording)
            {
                onRecordingStateChange(Obs, OutputState.Started);
            }
            else
            {
                onRecordingStateChange(Obs, OutputState.Stopped);
            }

        }

        private void onDisconnect(object sender, EventArgs e)
        {
            Console.WriteLine("Web Socket Disconnect");
        }

        private void onSceneChange(OBSWebsocket sender, string newSceneName)
        {

        }

        private void onSceneColChange(object sender, EventArgs e)
        {
            
        }

        private void onProfileChange(object sender, EventArgs e)
        {
            
        }

        private void onTransitionChange(OBSWebsocket sender, string newTransitionName)
        {
            
        }

        private void onTransitionDurationChange(OBSWebsocket sender, int newDuration)
        {
            
        }

        private void onStreamingStateChange(OBSWebsocket sender, OutputState newState)
        {
            string state = "";
            switch (newState)
            {
                case OutputState.Starting:
                    state = "Stream starting...";
                    break;

                case OutputState.Started:
                    state = "Stop streaming";
                    break;

                case OutputState.Stopping:
                    state = "Stream stopping...";
                    break;

                case OutputState.Stopped:
                    state = "Start streaming";
                    break;

                default:
                    state = "State unknown";
                    break;
            }
            Console.WriteLine(state);
        }

        private void onRecordingStateChange(OBSWebsocket sender, OutputState newState)
        {
            string state = "";
            switch (newState)
            {
                case OutputState.Starting:
                    state = "Recording starting...";
                    break;

                case OutputState.Started:
                    state = "Stop recording";
                    break;

                case OutputState.Stopping:
                    state = "Recording stopping...";
                    break;

                case OutputState.Stopped:
                    state = "Start recording";
                    break;

                default:
                    state = "State unknown";
                    break;
            }
            Console.WriteLine(state);
        }

        private void onStreamData(OBSWebsocket sender, StreamStatus data)
        {
            
        }
    }
}
