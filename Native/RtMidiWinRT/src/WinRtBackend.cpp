#include "InputSink.h"
#include "MidiBackend.h"

#include <winrt/Windows.Devices.Enumeration.h>
#include <winrt/Windows.Devices.Midi.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.Streams.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

using namespace winrt;
using namespace winrt::Windows::Devices::Enumeration;
using namespace winrt::Windows::Devices::Midi;
using namespace winrt::Windows::Foundation;

namespace rtmidi_winrt
{
    namespace
    {
        std::string Describe(const hresult_error& e)
        {
            char code[16];
            std::snprintf(code, sizeof(code), "0x%08X", static_cast<unsigned int>(e.code()));
            return to_string(e.message()) + " (" + code + ")";
        }

        std::string Describe(hresult code)
        {
            return Describe(hresult_error(code));
        }

        // Shared with the WinRT completion and event handlers, which can outlive the connection.
        struct PortState
        {
            std::mutex mutex;
            bool closed = false;
            IAsyncOperation<MidiInPort> pending { nullptr };
            MidiInPort port { nullptr };
            event_token messageToken {};
            std::shared_ptr<InputSink> sink;
            std::string name;
        };

        void OnMessage(const std::shared_ptr<InputSink>& sink, const MidiMessageReceivedEventArgs& args) noexcept
        {
            try
            {
                IMidiMessage message = args.Message();
                Windows::Storage::Streams::IBuffer raw = message.RawData();
                double seconds = std::chrono::duration<double>(message.Timestamp()).count();
                sink->Push(raw.data(), raw.Length(), seconds);
            }
            catch (const hresult_error& e)
            {
                sink->Fail("Failed to read a MIDI message: " + Describe(e));
            }
            catch (...)
            {
                sink->Fail("Failed to read a MIDI message");
            }
        }

        void OnOpened(const std::shared_ptr<PortState>& state, const IAsyncOperation<MidiInPort>& operation,
            AsyncStatus status) noexcept
        {
            MidiInPort port { nullptr };
            try
            {
                if (status == AsyncStatus::Completed)
                    port = operation.GetResults();
                else if (status == AsyncStatus::Canceled)
                    return;
                else
                {
                    state->sink->Fail("Failed to open MIDI port '" + state->name + "': "
                        + Describe(operation.ErrorCode()));
                    return;
                }

                if (port == nullptr)
                {
                    state->sink->Fail("MIDI port '" + state->name + "' is unavailable (removed or in use)");
                    return;
                }

                std::lock_guard lock(state->mutex);
                state->pending = nullptr;
                if (!state->closed)
                {
                    std::shared_ptr<InputSink> sink = state->sink;
                    state->messageToken = port.MessageReceived(
                        [sink](const MidiInPort&, const MidiMessageReceivedEventArgs& args) { OnMessage(sink, args); });
                    state->port = port;
                    return;
                }
            }
            catch (const hresult_error& e)
            {
                state->sink->Fail("Failed to open MIDI port '" + state->name + "': " + Describe(e));
            }
            catch (...)
            {
                state->sink->Fail("Failed to open MIDI port '" + state->name + "'");
            }

            try
            {
                if (port != nullptr)
                    port.Close();
            }
            catch (...)
            {
            }
        }

        class WinRtInputConnection final : public InputConnection
        {
        public:
            explicit WinRtInputConnection(std::shared_ptr<PortState> state)
                : _state(std::move(state))
            {
            }

            ~WinRtInputConnection() override
            {
                IAsyncOperation<MidiInPort> pending { nullptr };
                MidiInPort port { nullptr };
                event_token token {};
                {
                    std::lock_guard lock(_state->mutex);
                    _state->closed = true;
                    pending = std::exchange(_state->pending, nullptr);
                    port = std::exchange(_state->port, nullptr);
                    token = _state->messageToken;
                }

                // Outside the lock: Cancel can run the completion handler inline, which takes it.
                try
                {
                    if (pending != nullptr)
                        pending.Cancel();
                }
                catch (...)
                {
                }

                try
                {
                    if (port != nullptr)
                    {
                        port.MessageReceived(token);
                        port.Close();
                    }
                }
                catch (...)
                {
                }
            }

        private:
            std::shared_ptr<PortState> _state;
        };

        struct PortEntry
        {
            hstring id;
            std::string name;
        };

        class WinRtBackend final : public MidiBackend
        {
        public:
            WinRtBackend()
            {
                try
                {
                    _watcher = DeviceInformation::CreateWatcher(MidiInPort::GetDeviceSelector());
                    // DeviceWatcher only reports post-enumeration changes when Added, Removed and Updated
                    // all have handlers.
                    _watcher.Added([this](const DeviceWatcher&, const DeviceInformation& info) { OnAdded(info); });
                    _watcher.Removed(
                        [this](const DeviceWatcher&, const DeviceInformationUpdate& update) { OnRemoved(update); });
                    _watcher.Updated([](const DeviceWatcher&, const DeviceInformationUpdate&) {});
                    _watcher.EnumerationCompleted([this](const DeviceWatcher&, const IInspectable&) {
                        std::lock_guard lock(_mutex);
                        _enumerated = true;
                    });
                    _watcher.Start();
                }
                catch (const hresult_error& e)
                {
                    SetStartupError(Describe(e));
                }
                catch (...)
                {
                    SetStartupError("unknown error");
                }
            }

            std::string StartupError() override
            {
                std::lock_guard lock(_mutex);
                return _startupError;
            }

            // Hidden until the initial enumeration completes, so Minis rebuilds its devices once at
            // startup instead of once per device.
            unsigned int InputPortCount() override
            {
                std::lock_guard lock(_mutex);
                return _enumerated ? static_cast<unsigned int>(_ports.size()) : 0;
            }

            bool InputPortName(unsigned int index, std::string& name) override
            {
                std::lock_guard lock(_mutex);
                if (!_enumerated || index >= _ports.size())
                    return false;
                name = _ports[index].name;
                return true;
            }

            std::unique_ptr<InputConnection> OpenInput(
                unsigned int index, std::shared_ptr<InputSink> sink, std::string& error) override
            {
                try
                {
                    auto state = std::make_shared<PortState>();
                    hstring id;
                    {
                        std::lock_guard lock(_mutex);
                        if (!_enumerated || index >= _ports.size())
                        {
                            error = "MIDI port " + std::to_string(index) + " does not exist";
                            return nullptr;
                        }
                        id = _ports[index].id;
                        state->name = _ports[index].name;
                    }
                    state->sink = std::move(sink);

                    // Never block here: Minis opens ports from the main thread, which may be an STA.
                    IAsyncOperation<MidiInPort> operation = MidiInPort::FromIdAsync(id);
                    {
                        std::lock_guard lock(state->mutex);
                        state->pending = operation;
                    }
                    auto connection = std::make_unique<WinRtInputConnection>(state);
                    operation.Completed([state](const IAsyncOperation<MidiInPort>& op, AsyncStatus status) {
                        OnOpened(state, op, status);
                    });
                    return connection;
                }
                catch (const hresult_error& e)
                {
                    error = "Failed to open MIDI port " + std::to_string(index) + ": " + Describe(e);
                }
                catch (const std::exception& e)
                {
                    error = "Failed to open MIDI port " + std::to_string(index) + ": " + e.what();
                }
                catch (...)
                {
                    error = "Failed to open MIDI port " + std::to_string(index);
                }
                return nullptr;
            }

        private:
            void SetStartupError(std::string error) noexcept
            {
                try
                {
                    std::lock_guard lock(_mutex);
                    _startupError = std::move(error);
                }
                catch (...)
                {
                }
            }

            void OnAdded(const DeviceInformation& info) noexcept
            {
                try
                {
                    PortEntry entry { info.Id(), to_string(info.Name()) };
                    std::lock_guard lock(_mutex);
                    auto existing = std::find_if(
                        _ports.begin(), _ports.end(), [&](const PortEntry& port) { return port.id == entry.id; });
                    if (existing == _ports.end())
                        _ports.push_back(std::move(entry));
                }
                catch (...)
                {
                }
            }

            void OnRemoved(const DeviceInformationUpdate& update) noexcept
            {
                try
                {
                    hstring id = update.Id();
                    std::lock_guard lock(_mutex);
                    _ports.erase(std::remove_if(_ports.begin(), _ports.end(),
                                     [&](const PortEntry& port) { return port.id == id; }),
                        _ports.end());
                }
                catch (...)
                {
                }
            }

            std::mutex _mutex;
            DeviceWatcher _watcher { nullptr };
            std::vector<PortEntry> _ports;
            bool _enumerated = false;
            std::string _startupError;
        };
    }

    MidiBackend& GetMidiBackend()
    {
        // Leaked on purpose: WinRT objects must not be released during DLL unload.
        static WinRtBackend* backend = new WinRtBackend();
        return *backend;
    }
}
