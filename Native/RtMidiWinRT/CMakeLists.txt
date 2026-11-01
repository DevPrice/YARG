cmake_minimum_required(VERSION 3.25)
project(RtMidiWinRT VERSION 0.1.0 LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "RtMidiWinRT targets 64-bit Windows only")
endif()

set(RTMIDI_WINRT_CORE_SOURCES
    src/InputSink.cpp
    src/rtmidi_c.cpp
)

function(rtmidi_winrt_configure target)
    target_compile_features(${target} PRIVATE cxx_std_20)
    target_compile_definitions(${target} PRIVATE RTMIDI_WINRT_BUILD WIN32_LEAN_AND_MEAN NOMINMAX)
    target_include_directories(${target} PRIVATE src)
    target_compile_options(${target} PRIVATE /W4 /permissive- /EHsc /Brepro /utf-8)
    target_link_options(${target} PRIVATE /Brepro)
endfunction()

if(CMAKE_SYSTEM_NAME STREQUAL "WindowsStore")
    # The store toolset ships no static CRT; the app CRT comes from the Microsoft.VCLibs framework
    # package that Unity's UWP project already depends on.
    add_library(RtMidi SHARED ${RTMIDI_WINRT_CORE_SOURCES} src/WinRtBackend.cpp)
    rtmidi_winrt_configure(RtMidi)
    set_property(TARGET RtMidi PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
else()
    # A desktop process can't load the app-partition DLL, so the tests link the WinRT-free core
    # against a fake backend instead.
    enable_testing()
    add_executable(RtMidiCoreTests ${RTMIDI_WINRT_CORE_SOURCES} tests/RtMidiCoreTests.cpp)
    rtmidi_winrt_configure(RtMidiCoreTests)
    target_link_libraries(RtMidiCoreTests PRIVATE ole32.lib)
    add_test(NAME RtMidiCoreTests COMMAND RtMidiCoreTests)
endif()
