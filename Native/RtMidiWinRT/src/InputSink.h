#pragma once

#include <cstddef>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace rtmidi_winrt
{
    using MessageCallback = void (*)(double deltaSeconds, const unsigned char* message, size_t size, void* userData);

    // Per-handle input queue. Push, Fail and the callback run on WinRT thread-pool threads;
    // Pop runs on whichever thread polls rtmidi_in_get_message.
    class InputSink
    {
    public:
        // rtmidi's own default for RtMidiIn.
        static constexpr size_t DefaultQueueLimit = 100;

        enum class PopResult
        {
            Empty,
            Message,
            BufferTooSmall,
            Failed,
        };

        explicit InputSink(size_t queueLimit = DefaultQueueLimit);

        // Applies rtmidi's default ignore flags (SysEx, MIDI time code/clock, active sensing),
        // then hands the message to the callback if one is set, or queues it.
        void Push(const uint8_t* data, size_t size, double timestampSeconds);

        // Sticky: Pop reports it once the queue has drained, until Reset.
        void Fail(std::string message);

        // On BufferTooSmall the message is dropped, so one oversized message can't wedge the queue.
        PopResult Pop(uint8_t* buffer, size_t& size, double& deltaSeconds, std::string& error);

        bool SetCallback(MessageCallback callback, void* userData);
        void CancelCallback();

        // Drops queued messages, the failure and the timestamp history; keeps the callback.
        void Reset();

        size_t DroppedCount() const;

        static bool IsIgnoredByDefault(uint8_t status);

    private:
        struct Message
        {
            std::vector<uint8_t> bytes;
            double deltaSeconds;
        };

        mutable std::mutex _mutex;
        std::deque<Message> _queue;
        const size_t _queueLimit;
        size_t _dropped = 0;
        bool _hasLastTimestamp = false;
        double _lastTimestamp = 0.0;
        bool _failed = false;
        std::string _error;

        // Held while the callback runs, so CancelCallback returns only once no call is in flight.
        std::mutex _callbackMutex;
        MessageCallback _callback = nullptr;
        void* _callbackUserData = nullptr;
    };
}
