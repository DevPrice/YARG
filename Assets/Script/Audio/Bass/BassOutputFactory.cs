#nullable enable
using System;
using System.Collections.Generic;
using YARG.Audio.BASS.Asio;
using YARG.Audio.BASS.Wasapi;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Helpers;

namespace YARG.Audio.BASS
{
    /// <summary>
    ///     Creates shared, WASAPI, or ASIO outputs from the device name selected in settings.
    /// </summary>
    internal sealed class BassOutputFactory
    {
        private readonly BassAsioMics         _asioMics = new();
        private readonly BassWasapiMicManager _wasapiMics;

        private readonly BassAudioRouter _router;

        public BassOutputFactory(BassAudioRouter router)
        {
            _router = router;
            _wasapiMics = new BassWasapiMicManager(router);
        }

        public BassOutput? Create(string name)
        {
            if (PlatformFeatures.SupportsWasapi && BassWasapiOutput.IsWasapiDevice(name))
            {
                return BassWasapiOutput.Find(name, _wasapiMics);
            }

            if (PlatformFeatures.SupportsAsio && BassAsioOutput.IsAsioDevice(name))
            {
                return BassAsioOutput.Find(name, _router, _asioMics);
            }

            return BassSharedOutput.Find(name, _router);
        }

        public List<(int id, string name)> GetAllDevices()
        {
            var devices = BassSharedOutput.GetDevices();
            if (PlatformFeatures.SupportsAsio)
            {
                devices.AddRange(BassAsioOutput.GetDevices());
            }

            if (PlatformFeatures.SupportsWasapi)
            {
                devices.AddRange(BassWasapiOutput.GetDevices());
            }

            return devices;
        }

        public AudioOutputMode ModeFor(string name)
        {
            // TODO: This is a hacky workaround for a failure to deserialize settings on Mac. Most likely
            //  the problem is actually that we are getting called with a null name or that BassWasapiOutput
            //  hasn't been created yet.
            try
            {
                if (BassWasapiOutput.IsWasapiDevice(name))
                {
                    return AudioOutputMode.WasapiExclusive;
                }

                if (BassAsioOutput.IsAsioDevice(name))
                {
                    return AudioOutputMode.Asio;
                }
            }
            catch (Exception)
            {
                YargLogger.LogError("BassAsioOutput or BassWasapiOutput was not initialized! Falling back to Shared.");
            }

            return AudioOutputMode.Shared;
        }

        public void Dispose() => _wasapiMics.Dispose();
    }
}
