#pragma once

#include <memory>
#include <string>

namespace rtmidi_winrt
{
    class InputSink;

    // Destroying it closes the port and stops delivery to the sink.
    class InputConnection
    {
    public:
        virtual ~InputConnection() = default;
    };

    // Implementations must be thread-safe and must not throw.
    class MidiBackend
    {
    public:
        virtual ~MidiBackend() = default;

        // Non-empty if device enumeration couldn't start; the backend then reports no ports.
        virtual std::string StartupError() = 0;

        // Must not block: Minis calls it from the main thread every input update.
        virtual unsigned int InputPortCount() = 0;
        virtual bool InputPortName(unsigned int index, std::string& name) = 0;

        // May finish opening asynchronously; a late failure is reported through InputSink::Fail.
        virtual std::unique_ptr<InputConnection> OpenInput(
            unsigned int index, std::shared_ptr<InputSink> sink, std::string& error) = 0;
    };

    // The process-wide backend, created on first use and never destroyed.
    MidiBackend& GetMidiBackend();
}
