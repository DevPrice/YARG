// Exercises the C ABI and InputSink against a fake backend; the WinRT backend can only load in an
// app container.

#include "InputSink.h"
#include "MidiBackend.h"
#include "rtmidi_c.h"

#include <combaseapi.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

using namespace rtmidi_winrt;

namespace
{
    int g_failures = 0;

#define CHECK(condition)                                                                   \
    do                                                                                     \
    {                                                                                      \
        if (!(condition))                                                                  \
        {                                                                                  \
            std::printf("%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #condition);       \
            ++g_failures;                                                                  \
        }                                                                                  \
    } while (0)

    class FakeConnection final : public InputConnection
    {
    public:
        explicit FakeConnection(int* openCount)
            : _openCount(openCount)
        {
            ++*_openCount;
        }

        ~FakeConnection() override
        {
            --*_openCount;
        }

    private:
        int* _openCount;
    };

    class FakeBackend final : public MidiBackend
    {
    public:
        std::vector<std::string> ports;
        std::string startupError;
        std::shared_ptr<InputSink> lastSink;
        int openConnections = 0;

        std::string StartupError() override
        {
            return startupError;
        }

        unsigned int InputPortCount() override
        {
            return static_cast<unsigned int>(ports.size());
        }

        bool InputPortName(unsigned int index, std::string& name) override
        {
            if (index >= ports.size())
                return false;
            name = ports[index];
            return true;
        }

        std::unique_ptr<InputConnection> OpenInput(
            unsigned int index, std::shared_ptr<InputSink> sink, std::string& error) override
        {
            if (index >= ports.size())
            {
                error = "no such port";
                return nullptr;
            }
            lastSink = std::move(sink);
            return std::make_unique<FakeConnection>(&openConnections);
        }
    };

    FakeBackend g_backend;

    void Push(std::initializer_list<uint8_t> bytes, double timestamp)
    {
        std::vector<uint8_t> data(bytes);
        g_backend.lastSink->Push(data.data(), data.size(), timestamp);
    }

    void TestCreateAndFree()
    {
        g_backend = FakeBackend {};
        RtMidiInPtr in = rtmidi_in_create_default();
        CHECK(in != nullptr);
        CHECK(in->ok);
        CHECK(in->msg != nullptr);
        rtmidi_in_free(in);
        rtmidi_in_free(nullptr);
        rtmidi_out_free(nullptr);
    }

    void TestStartupErrorReportedOnCreate()
    {
        g_backend = FakeBackend {};
        g_backend.startupError = "watcher failed";
        RtMidiInPtr in = rtmidi_in_create_default();
        CHECK(in != nullptr);
        CHECK(!in->ok);
        CHECK(std::strstr(in->msg, "watcher failed") != nullptr);

        CHECK(rtmidi_get_port_count(in) == 0);
        CHECK(in->ok);
        rtmidi_in_free(in);
    }

    void TestPortEnumeration()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17", "Other" };
        RtMidiInPtr in = rtmidi_in_create_default();
        CHECK(rtmidi_get_port_count(in) == 2);
        CHECK(in->ok);

        const char* name = rtmidi_get_port_name(in, 1);
        CHECK(in->ok);
        CHECK(name != nullptr && std::strcmp(name, "Other") == 0);
        CoTaskMemFree(const_cast<char*>(name));

        CHECK(rtmidi_get_port_name(in, 2) == nullptr);
        CHECK(!in->ok);
        CHECK(in->msg != nullptr && in->msg[0] != '\0');

        CHECK(rtmidi_get_port_count(in) == 2);
        CHECK(in->ok);
        rtmidi_in_free(in);
    }

    void TestMessagesQueueInOrderWithDeltas()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17" };
        RtMidiInPtr in = rtmidi_in_create_default();
        rtmidi_open_port(in, 0, "RtMidi Input");
        CHECK(in->ok);
        CHECK(g_backend.openConnections == 1);

        uint8_t buffer[1024];
        size_t size = sizeof(buffer);
        CHECK(rtmidi_in_get_message(in, buffer, &size) == 0.0);
        CHECK(size == 0);
        CHECK(in->ok);

        Push({ 0x99, 38, 100 }, 1.0);
        Push({ 0xFE }, 1.1);
        Push({ 0xF8 }, 1.2);
        Push({ 0xF0, 0x41, 0xF7 }, 1.3);
        Push({ 0x89, 38, 0 }, 1.25);

        size = sizeof(buffer);
        double delta = rtmidi_in_get_message(in, buffer, &size);
        CHECK(in->ok);
        CHECK(size == 3 && buffer[0] == 0x99 && buffer[1] == 38 && buffer[2] == 100);
        CHECK(delta == 0.0);

        size = sizeof(buffer);
        delta = rtmidi_in_get_message(in, buffer, &size);
        CHECK(size == 3 && buffer[0] == 0x89);
        CHECK(delta > 0.2499 && delta < 0.2501);

        size = sizeof(buffer);
        rtmidi_in_get_message(in, buffer, &size);
        CHECK(size == 0);
        CHECK(in->ok);

        rtmidi_in_free(in);
        CHECK(g_backend.openConnections == 0);
    }

    void TestOversizedMessageIsDroppedNotWedged()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17" };
        RtMidiInPtr in = rtmidi_in_create_default();
        rtmidi_open_port(in, 0, "RtMidi Input");

        Push({ 0x99, 38, 100 }, 0.0);
        Push({ 0x99, 40, 90 }, 0.0);

        uint8_t buffer[2];
        size_t size = sizeof(buffer);
        CHECK(rtmidi_in_get_message(in, buffer, &size) == -1.0);
        CHECK(!in->ok);
        CHECK(size == 0);

        uint8_t big[3];
        size = sizeof(big);
        rtmidi_in_get_message(in, big, &size);
        CHECK(in->ok);
        CHECK(size == 3 && big[1] == 40);
        rtmidi_in_free(in);
    }

    void TestAsyncFailureIsStickyAfterDrain()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17" };
        RtMidiInPtr in = rtmidi_in_create_default();
        rtmidi_open_port(in, 0, "RtMidi Input");
        CHECK(in->ok);

        Push({ 0x99, 38, 100 }, 0.0);
        g_backend.lastSink->Fail("port vanished");

        uint8_t buffer[16];
        size_t size = sizeof(buffer);
        rtmidi_in_get_message(in, buffer, &size);
        CHECK(in->ok && size == 3);

        size = sizeof(buffer);
        CHECK(rtmidi_in_get_message(in, buffer, &size) == -1.0);
        CHECK(!in->ok && size == 0);
        CHECK(std::strstr(in->msg, "port vanished") != nullptr);

        size = sizeof(buffer);
        rtmidi_in_get_message(in, buffer, &size);
        CHECK(!in->ok);

        rtmidi_close_port(in);
        CHECK(in->ok);
        rtmidi_open_port(in, 0, "RtMidi Input");
        size = sizeof(buffer);
        rtmidi_in_get_message(in, buffer, &size);
        CHECK(in->ok && size == 0);
        rtmidi_in_free(in);
    }

    void TestOpenErrors()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17" };
        RtMidiInPtr in = rtmidi_in_create_default();
        rtmidi_open_port(in, 5, "RtMidi Input");
        CHECK(!in->ok);
        CHECK(std::strcmp(in->msg, "no such port") == 0);

        rtmidi_open_port(in, 0, "RtMidi Input");
        CHECK(in->ok);
        rtmidi_open_port(in, 0, "RtMidi Input");
        CHECK(!in->ok);
        CHECK(g_backend.openConnections == 1);

        rtmidi_close_port(in);
        CHECK(g_backend.openConnections == 0);
        rtmidi_in_free(in);
    }

    void TestQueueLimit()
    {
        InputSink sink(2);
        const uint8_t note[] = { 0x90, 60, 1 };
        sink.Push(note, 3, 0.0);
        sink.Push(note, 3, 0.0);
        sink.Push(note, 3, 0.0);
        CHECK(sink.DroppedCount() == 1);
    }

    struct CallbackLog
    {
        int calls = 0;
        uint8_t lastStatus = 0;
    };

    void RecordCallback(double, const unsigned char* message, size_t size, void* userData)
    {
        auto* log = static_cast<CallbackLog*>(userData);
        ++log->calls;
        log->lastStatus = size > 0 ? message[0] : 0;
    }

    void TestCallback()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17" };
        RtMidiInPtr in = rtmidi_in_create_default();
        rtmidi_open_port(in, 0, "RtMidi Input");

        CallbackLog log;
        rtmidi_in_set_callback(in, RecordCallback, &log);
        CHECK(in->ok);
        rtmidi_in_set_callback(in, RecordCallback, &log);
        CHECK(!in->ok);

        Push({ 0xB9, 4, 64 }, 0.0);
        Push({ 0xFE }, 0.0);
        CHECK(log.calls == 1 && log.lastStatus == 0xB9);

        uint8_t buffer[16];
        size_t size = sizeof(buffer);
        rtmidi_in_get_message(in, buffer, &size);
        CHECK(size == 0);

        rtmidi_in_cancel_callback(in);
        CHECK(in->ok);
        Push({ 0x99, 38, 1 }, 0.0);
        CHECK(log.calls == 1);
        size = sizeof(buffer);
        rtmidi_in_get_message(in, buffer, &size);
        CHECK(size == 3);
        rtmidi_in_free(in);
    }

    void TestOutputIsUnsupported()
    {
        g_backend = FakeBackend {};
        g_backend.ports = { "TD-17" };
        RtMidiOutPtr out = rtmidi_out_create_default();
        CHECK(out != nullptr && out->ok);
        CHECK(rtmidi_get_port_count(out) == 0);
        CHECK(out->ok);
        rtmidi_open_port(out, 0, "RtMidi Output");
        CHECK(!out->ok);
        const unsigned char message[] = { 0x90, 60, 1 };
        CHECK(rtmidi_out_send_message(out, message, 3) == -1);
        CHECK(!out->ok);
        rtmidi_out_free(out);
    }

    void TestNullHandles()
    {
        size_t size = 4;
        uint8_t buffer[4];
        CHECK(rtmidi_get_port_count(nullptr) == 0);
        CHECK(rtmidi_get_port_name(nullptr, 0) == nullptr);
        CHECK(rtmidi_in_get_message(nullptr, buffer, &size) == -1.0);
        rtmidi_open_port(nullptr, 0, nullptr);
        rtmidi_close_port(nullptr);
        rtmidi_in_set_callback(nullptr, nullptr, nullptr);
        rtmidi_in_cancel_callback(nullptr);
        CHECK(rtmidi_out_send_message(nullptr, nullptr, 0) == -1);

        g_backend = FakeBackend {};
        RtMidiInPtr in = rtmidi_in_create_default();
        CHECK(rtmidi_in_get_message(in, buffer, nullptr) == -1.0);
        CHECK(!in->ok);
        rtmidi_in_free(in);
    }
}

namespace rtmidi_winrt
{
    MidiBackend& GetMidiBackend()
    {
        return g_backend;
    }
}

int main()
{
    TestCreateAndFree();
    TestStartupErrorReportedOnCreate();
    TestPortEnumeration();
    TestMessagesQueueInOrderWithDeltas();
    TestOversizedMessageIsDroppedNotWedged();
    TestAsyncFailureIsStickyAfterDrain();
    TestOpenErrors();
    TestQueueLimit();
    TestCallback();
    TestOutputIsUnsupported();
    TestNullHandles();

    if (g_failures != 0)
    {
        std::printf("%d check(s) failed\n", g_failures);
        return 1;
    }
    std::printf("All RtMidiWinRT core tests passed\n");
    return 0;
}
