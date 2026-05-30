#include <windows.h>
#include <dbghelp.h>
#include <tlhelp32.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

struct Args
{
    std::wstring command;
    DWORD pid = 0;
    std::wstring dumpType = L"mini";
    fs::path output;
    bool json = false;
};

std::wstring GetArgValue(const std::vector<std::wstring>& args, const std::wstring& name)
{
    for (size_t i = 0; i + 1 < args.size(); ++i)
    {
        if (args[i] == name)
        {
            return args[i + 1];
        }
    }

    return {};
}

std::wstring EscapeJson(const std::wstring& value)
{
    std::wstringstream escaped;
    for (const auto ch : value)
    {
        switch (ch)
        {
        case L'\\': escaped << L"\\\\"; break;
        case L'"': escaped << L"\\\""; break;
        case L'\n': escaped << L"\\n"; break;
        case L'\r': escaped << L"\\r"; break;
        case L'\t': escaped << L"\\t"; break;
        default: escaped << ch; break;
        }
    }
    return escaped.str();
}

std::wstring GetLastErrorMessage()
{
    const DWORD error = GetLastError();
    wchar_t* buffer = nullptr;
    FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        error,
        MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        reinterpret_cast<LPWSTR>(&buffer),
        0,
        nullptr);

    std::wstring message = buffer ? buffer : L"Unknown error";
    if (buffer)
    {
        LocalFree(buffer);
    }

    return message;
}

std::wstring GetProcessPath(DWORD pid)
{
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process)
    {
        return {};
    }

    wchar_t path[MAX_PATH];
    DWORD size = MAX_PATH;
    std::wstring result;
    if (QueryFullProcessImageNameW(process, 0, path, &size))
    {
        result.assign(path, size);
    }

    CloseHandle(process);
    return result;
}

std::wstring CreateGuidString()
{
    GUID guid{};
    if (CoCreateGuid(&guid) != S_OK)
    {
        return std::to_wstring(GetTickCount64());
    }

    wchar_t buffer[39]{};
    StringFromGUID2(guid, buffer, static_cast<int>(std::size(buffer)));
    std::wstring value(buffer);
    if (value.starts_with(L"{") && value.ends_with(L"}"))
    {
        value = value.substr(1, value.size() - 2);
    }
    return value;
}

std::wstring UtcNowIso8601()
{
    SYSTEMTIME utc{};
    GetSystemTime(&utc);

    wchar_t buffer[40]{};
    swprintf_s(
        buffer,
        L"%04u-%02u-%02uT%02u:%02u:%02uZ",
        utc.wYear,
        utc.wMonth,
        utc.wDay,
        utc.wHour,
        utc.wMinute,
        utc.wSecond);
    return buffer;
}

std::wstring GetMachineName()
{
    wchar_t buffer[MAX_COMPUTERNAME_LENGTH + 1]{};
    DWORD size = static_cast<DWORD>(std::size(buffer));
    return GetComputerNameW(buffer, &size) ? std::wstring(buffer, size) : L"Unknown";
}

std::wstring GetFileName(const std::wstring& path, DWORD pid)
{
    if (path.empty())
    {
        return L"pid-" + std::to_wstring(pid);
    }

    return fs::path(path).filename().wstring();
}

std::wstring FormatExceptionCode(DWORD exceptionCode)
{
    wchar_t buffer[16]{};
    swprintf_s(buffer, L"0x%08X", exceptionCode);
    return buffer;
}

int ListProcesses(bool json)
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE)
    {
        std::wcerr << L"CreateToolhelp32Snapshot failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);

    if (!Process32FirstW(snapshot, &entry))
    {
        CloseHandle(snapshot);
        std::wcerr << L"Process32FirstW failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    if (json)
    {
        std::wcout << L"[\n";
    }

    bool first = true;
    do
    {
        const auto path = GetProcessPath(entry.th32ProcessID);
        if (json)
        {
            if (!first)
            {
                std::wcout << L",\n";
            }
            first = false;
            std::wcout
                << L"  {\"pid\":" << entry.th32ProcessID
                << L",\"name\":\"" << EscapeJson(entry.szExeFile)
                << L"\",\"path\":\"" << EscapeJson(path)
                << L"\",\"architecture\":\"Unknown\"}";
        }
        else
        {
            std::wcout << entry.th32ProcessID << L"\t" << entry.szExeFile << L"\t" << path << L"\n";
        }
    } while (Process32NextW(snapshot, &entry));

    if (json)
    {
        std::wcout << L"\n]\n";
    }

    CloseHandle(snapshot);
    return 0;
}

MINIDUMP_TYPE ResolveDumpType(const std::wstring& dumpType)
{
    return dumpType == L"full"
        ? static_cast<MINIDUMP_TYPE>(MiniDumpWithFullMemory | MiniDumpWithHandleData | MiniDumpWithThreadInfo)
        : static_cast<MINIDUMP_TYPE>(MiniDumpNormal | MiniDumpWithThreadInfo);
}

void WriteMetadata(
    const fs::path& metadataPath,
    const std::wstring& dumpId,
    DWORD pid,
    const std::wstring& processPath,
    const fs::path& dumpPath,
    const std::wstring& dumpType,
    const std::wstring& captureReason,
    const std::wstring& exceptionCode)
{
    const auto processName = GetFileName(processPath, pid);

    std::wofstream metadata(metadataPath);
    metadata
        << L"{\n"
        << L"  \"dumpId\": \"" << EscapeJson(dumpId) << L"\",\n"
        << L"  \"processName\": \"" << EscapeJson(processName) << L"\",\n"
        << L"  \"pid\": " << pid << L",\n"
        << L"  \"processPath\": \"" << EscapeJson(processPath) << L"\",\n"
        << L"  \"architecture\": \"Unknown\",\n"
        << L"  \"capturedAtUtc\": \"" << UtcNowIso8601() << L"\",\n"
        << L"  \"captureReason\": \"" << EscapeJson(captureReason) << L"\",\n"
        << L"  \"exceptionCode\": " << (exceptionCode.empty() ? L"null" : (L"\"" + EscapeJson(exceptionCode) + L"\"")) << L",\n"
        << L"  \"dumpType\": \"" << (dumpType == L"full" ? L"FullDump" : L"MiniDump") << L"\",\n"
        << L"  \"dumpFilePath\": \"" << EscapeJson(dumpPath.wstring()) << L"\",\n"
        << L"  \"machineName\": \"" << EscapeJson(GetMachineName()) << L"\",\n"
        << L"  \"osVersion\": \"Windows\",\n"
        << L"  \"doctorDumpVersion\": \"0.1.0\"\n"
        << L"}\n";
}

int CaptureDumpWithHandle(
    HANDLE process,
    DWORD pid,
    const fs::path& output,
    const std::wstring& dumpType,
    const std::wstring& captureReason,
    const std::wstring& exceptionCode)
{
    const auto dumpId = CreateGuidString();
    const auto captureFolder = output / dumpId;
    fs::create_directories(captureFolder);
    const auto dumpPath = captureFolder / (dumpId + L".dmp");
    const auto processPath = GetProcessPath(pid);

    HANDLE file = CreateFileW(
        dumpPath.c_str(),
        GENERIC_WRITE,
        0,
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (file == INVALID_HANDLE_VALUE)
    {
        std::wcerr << L"CreateFileW failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    const BOOL ok = MiniDumpWriteDump(process, pid, file, ResolveDumpType(dumpType), nullptr, nullptr, nullptr);
    CloseHandle(file);

    if (!ok)
    {
        std::wcerr << L"MiniDumpWriteDump failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    const auto metadataPath = captureFolder / L"metadata.json";
    WriteMetadata(metadataPath, dumpId, pid, processPath, dumpPath, dumpType, captureReason, exceptionCode);

    std::wcout << L"Captured dump: " << dumpPath.wstring() << L"\n";
    return 0;
}

int CaptureDump(DWORD pid, const fs::path& output, const std::wstring& dumpType)
{
    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_DUP_HANDLE, FALSE, pid);
    if (!process)
    {
        std::wcerr << L"OpenProcess failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    const auto result = CaptureDumpWithHandle(process, pid, output, dumpType, L"Manual", L"");
    CloseHandle(process);
    return result;
}

int MonitorProcess(DWORD pid, const fs::path& output, const std::wstring& dumpType)
{
    if (!DebugActiveProcess(pid))
    {
        std::wcerr << L"DebugActiveProcess failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    DebugSetProcessKillOnExit(FALSE);
    std::wcout << L"Monitoring process " << pid << L" for unhandled exceptions...\n";

    HANDLE processHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_DUP_HANDLE, FALSE, pid);
    bool captured = false;
    int result = 0;

    while (!captured)
    {
        DEBUG_EVENT debugEvent{};
        if (!WaitForDebugEvent(&debugEvent, INFINITE))
        {
            std::wcerr << L"WaitForDebugEvent failed: " << GetLastErrorMessage() << L"\n";
            result = 1;
            break;
        }

        DWORD continueStatus = DBG_CONTINUE;

        switch (debugEvent.dwDebugEventCode)
        {
        case CREATE_PROCESS_DEBUG_EVENT:
            if (processHandle == nullptr)
            {
                processHandle = debugEvent.u.CreateProcessInfo.hProcess;
            }
            else if (debugEvent.u.CreateProcessInfo.hProcess)
            {
                CloseHandle(debugEvent.u.CreateProcessInfo.hProcess);
            }
            if (debugEvent.u.CreateProcessInfo.hThread)
            {
                CloseHandle(debugEvent.u.CreateProcessInfo.hThread);
            }
            if (debugEvent.u.CreateProcessInfo.hFile)
            {
                CloseHandle(debugEvent.u.CreateProcessInfo.hFile);
            }
            break;

        case CREATE_THREAD_DEBUG_EVENT:
            if (debugEvent.u.CreateThread.hThread)
            {
                CloseHandle(debugEvent.u.CreateThread.hThread);
            }
            break;

        case LOAD_DLL_DEBUG_EVENT:
            if (debugEvent.u.LoadDll.hFile)
            {
                CloseHandle(debugEvent.u.LoadDll.hFile);
            }
            break;

        case EXCEPTION_DEBUG_EVENT:
        {
            const auto exception = debugEvent.u.Exception;
            if (exception.dwFirstChance == 0)
            {
                if (processHandle == nullptr)
                {
                    processHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_DUP_HANDLE, FALSE, pid);
                }

                if (processHandle == nullptr)
                {
                    std::wcerr << L"OpenProcess failed during crash capture: " << GetLastErrorMessage() << L"\n";
                    result = 1;
                }
                else
                {
                    result = CaptureDumpWithHandle(
                        processHandle,
                        pid,
                        output,
                        dumpType,
                        L"Crash",
                        FormatExceptionCode(exception.ExceptionRecord.ExceptionCode));
                }

                captured = true;
                continueStatus = DBG_EXCEPTION_NOT_HANDLED;
            }
            else
            {
                continueStatus = DBG_EXCEPTION_NOT_HANDLED;
            }
            break;
        }

        case EXIT_PROCESS_DEBUG_EVENT:
            std::wcerr << L"Process exited before an unhandled exception was observed.\n";
            captured = true;
            result = 2;
            break;

        default:
            continueStatus = DBG_CONTINUE;
            break;
        }

        ContinueDebugEvent(debugEvent.dwProcessId, debugEvent.dwThreadId, continueStatus);
    }

    DebugActiveProcessStop(pid);

    if (processHandle)
    {
        CloseHandle(processHandle);
    }

    return result;
}

void PrintUsage()
{
    std::wcout
        << L"DoctorDump.Agent\n\n"
        << L"Usage:\n"
        << L"  DoctorDump.Agent.exe list [--json]\n"
        << L"  DoctorDump.Agent.exe capture --pid 1234 --type mini --output C:\\Dumps\n"
        << L"  DoctorDump.Agent.exe monitor --pid 1234 --type mini --output C:\\Dumps\n";
}

Args ParseArgs(int argc, wchar_t* argv[])
{
    std::vector<std::wstring> values;
    for (int i = 1; i < argc; ++i)
    {
        values.emplace_back(argv[i]);
    }

    Args parsed;
    if (!values.empty())
    {
        parsed.command = values[0];
    }

    parsed.json = std::find(values.begin(), values.end(), L"--json") != values.end();
    parsed.dumpType = GetArgValue(values, L"--type");
    if (parsed.dumpType.empty())
    {
        parsed.dumpType = L"mini";
    }

    const auto pid = GetArgValue(values, L"--pid");
    if (!pid.empty())
    {
        parsed.pid = std::wcstoul(pid.c_str(), nullptr, 10);
    }

    parsed.output = GetArgValue(values, L"--output");
    return parsed;
}

int wmain(int argc, wchar_t* argv[])
{
    const auto args = ParseArgs(argc, argv);

    if (args.command == L"list")
    {
        return ListProcesses(args.json);
    }

    if (args.command == L"capture" && args.pid != 0 && !args.output.empty())
    {
        return CaptureDump(args.pid, args.output, args.dumpType);
    }

    if (args.command == L"monitor" && args.pid != 0 && !args.output.empty())
    {
        return MonitorProcess(args.pid, args.output, args.dumpType);
    }

    PrintUsage();
    return 2;
}
