#include "InputSink.h"

#include <cstring>
#include <utility>

namespace rtmidi_winrt
{
    InputSink::InputSink(size_t queueLimit)
        : _queueLimit(queueLimit)
    {
    }

    bool InputSink::IsIgnoredByDefault(uint8_t status)
    {
        switch (status)
        {
            case 0xF0: // SysEx
            case 0xF1: // MIDI time code quarter frame
            case 0xF8: // timing clock
            case 0xFE: // active sensing; Roland kits send it every 300 ms
                return true;
            default:
                return false;
        }
    }

    void InputSink::Push(const uint8_t* data, size_t size, double timestampSeconds)
    {
        if (data == nullptr || size == 0 || IsIgnoredByDefault(data[0]))
            return;

        std::lock_guard callbackLock(_callbackMutex);

        double delta;
        {
            std::lock_guard lock(_mutex);
            delta = _hasLastTimestamp ? timestampSeconds - _lastTimestamp : 0.0;
            if (delta < 0.0)
                delta = 0.0;
            _hasLastTimestamp = true;
            _lastTimestamp = timestampSeconds;

            if (_callback == nullptr)
            {
                if (_queue.size() >= _queueLimit)
                {
                    ++_dropped;
                    return;
                }
                _queue.push_back(Message { std::vector<uint8_t>(data, data + size), delta });
                return;
            }
        }

        _callback(delta, data, size, _callbackUserData);
    }

    void InputSink::Fail(std::string message)
    {
        std::lock_guard lock(_mutex);
        if (_failed)
            return;
        _failed = true;
        _error = std::move(message);
    }

    InputSink::PopResult InputSink::Pop(uint8_t* buffer, size_t& size, double& deltaSeconds, std::string& error)
    {
        std::lock_guard lock(_mutex);
        deltaSeconds = 0.0;

        if (_queue.empty())
        {
            size = 0;
            if (_failed)
            {
                error = _error;
                return PopResult::Failed;
            }
            return PopResult::Empty;
        }

        Message message = std::move(_queue.front());
        _queue.pop_front();

        if (buffer == nullptr || message.bytes.size() > size)
        {
            error = "MIDI message of " + std::to_string(message.bytes.size()) + " bytes does not fit the "
                + std::to_string(size) + "-byte buffer; message dropped";
            size = 0;
            return PopResult::BufferTooSmall;
        }

        std::memcpy(buffer, message.bytes.data(), message.bytes.size());
        size = message.bytes.size();
        deltaSeconds = message.deltaSeconds;
        return PopResult::Message;
    }

    bool InputSink::SetCallback(MessageCallback callback, void* userData)
    {
        std::lock_guard callbackLock(_callbackMutex);
        if (_callback != nullptr)
            return false;
        _callback = callback;
        _callbackUserData = userData;
        return true;
    }

    void InputSink::CancelCallback()
    {
        std::lock_guard callbackLock(_callbackMutex);
        _callback = nullptr;
        _callbackUserData = nullptr;
    }

    void InputSink::Reset()
    {
        std::lock_guard lock(_mutex);
        _queue.clear();
        _dropped = 0;
        _hasLastTimestamp = false;
        _lastTimestamp = 0.0;
        _failed = false;
        _error.clear();
    }

    size_t InputSink::DroppedCount() const
    {
        std::lock_guard lock(_mutex);
        return _dropped;
    }
}
