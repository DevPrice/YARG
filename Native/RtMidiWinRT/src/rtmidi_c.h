#pragma once

// The subset of the rtmidi 4.x C API (rtmidi_c.h) that Minis (jp.keijiro.minis, TheNathannator's
// yarg branch) binds in Runtime/Native/RtMidi.cs.

#include <stdbool.h>
#include <stddef.h>

#ifdef RTMIDI_WINRT_BUILD
#define RTMIDI_API __declspec(dllexport)
#else
#define RTMIDI_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Layout must match Minis.Native.RtMidiHandle.RtMidiWrapper: ptr, data, ok (U1), msg.
struct RtMidiWrapper
{
    void* ptr;
    void* data;
    bool ok;
    const char* msg;
};

typedef struct RtMidiWrapper* RtMidiPtr;
typedef struct RtMidiWrapper* RtMidiInPtr;
typedef struct RtMidiWrapper* RtMidiOutPtr;

typedef void (*RtMidiCCallback)(double timeStamp, const unsigned char* message, size_t messageSize, void* userData);

RTMIDI_API void rtmidi_open_port(RtMidiPtr device, unsigned int portNumber, const char* portName);
RTMIDI_API void rtmidi_close_port(RtMidiPtr device);
RTMIDI_API unsigned int rtmidi_get_port_count(RtMidiPtr device);
// The result is allocated with CoTaskMemAlloc: the P/Invoke marshaller frees a returned LPStr with
// CoTaskMemFree.
RTMIDI_API const char* rtmidi_get_port_name(RtMidiPtr device, unsigned int portNumber);

RTMIDI_API RtMidiInPtr rtmidi_in_create_default(void);
RTMIDI_API void rtmidi_in_free(RtMidiInPtr device);
RTMIDI_API void rtmidi_in_set_callback(RtMidiInPtr device, RtMidiCCallback callback, void* userData);
RTMIDI_API void rtmidi_in_cancel_callback(RtMidiInPtr device);
RTMIDI_API double rtmidi_in_get_message(RtMidiInPtr device, unsigned char* message, size_t* size);

RTMIDI_API RtMidiOutPtr rtmidi_out_create_default(void);
RTMIDI_API void rtmidi_out_free(RtMidiOutPtr device);
RTMIDI_API int rtmidi_out_send_message(RtMidiOutPtr device, const unsigned char* message, int length);

#ifdef __cplusplus
}
#endif
