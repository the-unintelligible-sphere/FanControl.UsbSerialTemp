using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace FanControl.UsbTemp
{
    internal class SerialThermometer : ITempSensorDriver
    {
        #region Private Fields
        private SerialPort _port;
        private List<byte> _bytes = new List<byte>();
        private Tuple<DateTime, float> _lastReading = null;

        private readonly TimeSpan MAXIMUM_READING_AGE = TimeSpan.FromSeconds(2);

        private readonly int MAXIMUM_READING_SIZE = 1024;
        #endregion

        #region ITempSensorDriver Implementation
        public void Open(string deviceId)
        {
            // Try configured port first
            if (TryOpenPort(deviceId))
                return;

            // Try all ports
            foreach (var port in SerialPort.GetPortNames())
            {
                if (TryOpenPort(port))
                    return;
            }

            // If nothing works, leave _port null
            // FanControl will still load the plugin
        }

        public void Close()
        {
            try 
            {
                if (_port != null)
                {
                    _port.DataReceived -= _port_DataReceived;
                    _port.Close();
                }
            } 
            catch 
            { 
            }
            _port = null;
            _bytes = null;
            _lastReading = null;
        }

        public float Temperature()
        {
            var lr = _lastReading;
            if (lr != null && (DateTime.UtcNow - lr.Item1) > MAXIMUM_READING_AGE)
            {
                return float.NaN;
            }

            return lr.Item2;
        }
        #endregion

        #region Private Methods
        private bool TryOpenPort(string portName)
        {
            try
            {
                SerialPort port = new SerialPort(portName, 115200);
                port.Open();
                if (!port.IsOpen)
                {
                    Close();
                }

                _port = port;
                _port.DataReceived += _port_DataReceived;
            }
            catch
            {
                // ignore
            }

            return _port != null;
        }

        private void _port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (e.EventType != SerialData.Chars)
            {
                return;
            }

            SerialPort sp = (SerialPort)sender;
            byte[] data = new byte[sp.BytesToRead];
            sp.Read(data, 0, data.Length);
            foreach (byte b in data)
            {
                if (b == '\n')
                {
                    string line = Encoding.ASCII.GetString(_bytes.ToArray());
                    _bytes.Clear();
                    if (float.TryParse(line, out float temp))
                    {
                        // We correctly parsed a value, record it and the time of the last reading
                        _lastReading = Tuple.Create(DateTime.UtcNow, temp);
                    }
                }
                else if (b != '\r')
                {
                    _bytes.Add(b);
                }

                // Protect ourselves from exhausting RAM if we have accidentally synced up to some massive source of serial data without newlines
                if (_bytes.Count > MAXIMUM_READING_SIZE)
                {
                    _bytes.Clear();
                }
            }
        }
        #endregion
    }
}
