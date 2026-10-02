#include "rtmidi_c.h"

#include "InputSink.h"
#include "MidiBackend.h"

#include <combaseapi.h>

#include <cstddef>
#include <cstring>
#include <exception>
#include <memory>
#include <new>
#include <string>
#include <utility>

static_assert(offsetof(RtMidiWrapper, ptr) == 0);
static_assert(offsetof(RtMidiWrapper, data) == 8);
static_assert(offsetof(RtMidiWrapper, ok) == 16);
static_assert(offsetof(RtMidiWrapper, msg) == 24);
static_assert(sizeof(RtMidiWrapper) == 32);

using namespace rtmidi_winrt;

namespace
{
    enum class Direction
    {
        Input,
        Output,
    };

    struct Device
    {
        RtMidiWrapper wrapper {};
        Direction direction;
        std::string error;
        std::shared_ptr<InputSink> sink;
        std::unique_ptr<InputConnection> connection;

        explicit Device(Direction direction)
            : direction(direction)
        {
            wrapper.ptr = this;
            wrapper.ok = true;
            wrapper.msg = "";
        }
    };

    Device* ToDevice(RtMidiWrapper* wrapper)
    {
        return wrapper != nullptr ? static_cast<Device*>(wrapper->ptr) : nullptr;
    }

    void SetError(Device& device, std::string message)
    {
        device.error = std::move(message);
        device.wrapper.msg = device.error.c_str();
        device.wrapper.ok = false;
    }

    // Every export funnels through here so no exception crosses the C ABI. Each call starts by
    // clearing the previous error, so a transient failure doesn't make Minis log on every later
    // frame.
    template <typename Result, typename Body>
    Result Guarded(RtMidiWrapper* wrapper, Result failure, Body&& body) noexcept
    {
        Device* device = ToDevice(wrapper);
        if (device == nullptr)
            return failure;

        device->wrapper.ok = true;
        try
        {
            return body(*device);
        }
        catch (const std::exception& e)
        {
            try
            {
                SetError(*device, e.what());
            }
            catch (...)
            {
                device->wrapper.msg = "out of memory";
                device->wrapper.ok = false;
            }
        }
        catch (...)
        {
            device->wrapper.msg = "unknown error";
            device->wrapper.ok = false;
        }
        return failure;
    }

    bool RequireInput(Device& device)
    {
        if (device.direction == Direction::Input)
            return true;
        SetError(device, "MIDI output is not supported on this platform");
        return false;
    }

    RtMidiWrapper* CreateDevice(Direction direction) noexcept
    {
        Device* device = new (std::nothrow) Device(direction);
        if (device == nullptr)
            return nullptr;

        if (direction == Direction::Input)
        {
            try
            {
                device->sink = std::make_shared<InputSink>();
                std::string error = GetMidiBackend().StartupError();
                if (!error.empty())
                    SetError(*device, "Failed to start MIDI device enumeration: " + error);
            }
            catch (...)
            {
                device->wrapper.msg = "out of memory";
                device->wrapper.ok = false;
            }
        }
        return &device->wrapper;
    }

    void FreeDevice(RtMidiWrapper* wrapper) noexcept
    {
        Device* device = ToDevice(wrapper);
        if (device == nullptr)
            return;

        try
        {
            device->connection.reset();
        }
        catch (...)
        {
        }
        delete device;
    }
}

extern "C"
{
    RTMIDI_API void rtmidi_open_port(RtMidiPtr wrapper, unsigned int portNumber, const char* /*portName*/)
    {
        Guarded(wrapper, 0, [&](Device& device) {
            if (!RequireInput(device))
                return 0;
            if (device.sink == nullptr)
            {
                SetError(device, "MIDI input was not initialized");
                return 0;
            }
            if (device.connection != nullptr)
            {
                SetError(device, "A MIDI port is already open on this handle");
                return 0;
            }

            device.sink->Reset();
            std::string error;
            device.connection = GetMidiBackend().OpenInput(portNumber, device.sink, error);
            if (device.connection == nullptr)
                SetError(device, error.empty() ? "Failed to open MIDI port" : error);
            return 0;
        });
    }

    RTMIDI_API void rtmidi_close_port(RtMidiPtr wrapper)
    {
        Guarded(wrapper, 0, [](Device& device) {
            device.connection.reset();
            if (device.sink != nullptr)
                device.sink->Reset();
            return 0;
        });
    }

    RTMIDI_API unsigned int rtmidi_get_port_count(RtMidiPtr wrapper)
    {
        return Guarded(wrapper, 0u, [](Device& device) {
            if (device.direction == Direction::Output)
                return 0u;
            return GetMidiBackend().InputPortCount();
        });
    }

    RTMIDI_API const char* rtmidi_get_port_name(RtMidiPtr wrapper, unsigned int portNumber)
    {
        return Guarded(wrapper, static_cast<const char*>(nullptr), [&](Device& device) -> const char* {
            if (!RequireInput(device))
                return nullptr;

            std::string name;
            if (!GetMidiBackend().InputPortName(portNumber, name))
            {
                SetError(device, "MIDI port " + std::to_string(portNumber) + " does not exist");
                return nullptr;
            }

            auto* result = static_cast<char*>(CoTaskMemAlloc(name.size() + 1));
            if (result == nullptr)
            {
                SetError(device, "out of memory");
                return nullptr;
            }
            std::memcpy(result, name.c_str(), name.size() + 1);
            return result;
        });
    }

    RTMIDI_API RtMidiInPtr rtmidi_in_create_default(void)
    {
        return CreateDevice(Direction::Input);
    }

    RTMIDI_API void rtmidi_in_free(RtMidiInPtr wrapper)
    {
        FreeDevice(wrapper);
    }

    RTMIDI_API void rtmidi_in_set_callback(RtMidiInPtr wrapper, RtMidiCCallback callback, void* userData)
    {
        Guarded(wrapper, 0, [&](Device& device) {
            if (!RequireInput(device) || device.sink == nullptr)
                return 0;
            if (callback == nullptr)
                SetError(device, "The MIDI callback is null");
            else if (!device.sink->SetCallback(callback, userData))
                SetError(device, "A MIDI callback is already set");
            return 0;
        });
    }

    RTMIDI_API void rtmidi_in_cancel_callback(RtMidiInPtr wrapper)
    {
        Guarded(wrapper, 0, [](Device& device) {
            if (RequireInput(device) && device.sink != nullptr)
                device.sink->CancelCallback();
            return 0;
        });
    }

    RTMIDI_API double rtmidi_in_get_message(RtMidiInPtr wrapper, unsigned char* message, size_t* size)
    {
        return Guarded(wrapper, -1.0, [&](Device& device) {
            if (size == nullptr)
            {
                SetError(device, "The size pointer is null");
                return -1.0;
            }
            if (!RequireInput(device) || device.sink == nullptr)
            {
                *size = 0;
                return -1.0;
            }

            double delta = 0.0;
            std::string error;
            switch (device.sink->Pop(message, *size, delta, error))
            {
                case InputSink::PopResult::Message:
                    return delta;
                case InputSink::PopResult::Empty:
                    return 0.0;
                case InputSink::PopResult::BufferTooSmall:
                case InputSink::PopResult::Failed:
                default:
                    SetError(device, std::move(error));
                    return -1.0;
            }
        });
    }

    RTMIDI_API RtMidiOutPtr rtmidi_out_create_default(void)
    {
        return CreateDevice(Direction::Output);
    }

    RTMIDI_API void rtmidi_out_free(RtMidiOutPtr wrapper)
    {
        FreeDevice(wrapper);
    }

    RTMIDI_API int rtmidi_out_send_message(RtMidiOutPtr wrapper, const unsigned char* /*message*/, int /*length*/)
    {
        return Guarded(wrapper, -1, [](Device& device) {
            SetError(device, "MIDI output is not supported on this platform");
            return -1;
        });
    }
}
