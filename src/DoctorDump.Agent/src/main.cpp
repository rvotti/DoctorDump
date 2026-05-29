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

int CaptureDump(DWORD pid, const fs::path& output, const std::wstring& dumpType)
{
    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_DUP_HANDLE, FALSE, pid);
    if (!process)
    {
        std::wcerr << L"OpenProcess failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    const auto dumpId = CreateGuidString();
    const auto captureFolder = output / dumpId;
    fs::create_directories(captureFolder);
    const auto dumpPath = captureFolder / (dumpId + L".dmp");
    const auto processPath = GetProcessPath(pid);
    const auto processName = GetFileName(processPath, pid);

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
        CloseHandle(process);
        std::wcerr << L"CreateFileW failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    const MINIDUMP_TYPE type = dumpType == L"full"
        ? static_cast<MINIDUMP_TYPE>(MiniDumpWithFullMemory | MiniDumpWithHandleData | MiniDumpWithThreadInfo)
        : static_cast<MINIDUMP_TYPE>(MiniDumpNormal | MiniDumpWithThreadInfo);

    const BOOL ok = MiniDumpWriteDump(process, pid, file, type, nullptr, nullptr, nullptr);
    CloseHandle(file);
    CloseHandle(process);

    if (!ok)
    {
        std::wcerr << L"MiniDumpWriteDump failed: " << GetLastErrorMessage() << L"\n";
        return 1;
    }

    const auto metadataPath = captureFolder / L"metadata.json";
    std::wofstream metadata(metadataPath);
    metadata
        << L"{\n"
        << L"  \"dumpId\": \"" << EscapeJson(dumpId) << L"\",\n"
        << L"  \"processName\": \"" << EscapeJson(processName) << L"\",\n"
        << L"  \"pid\": " << pid << L",\n"
        << L"  \"processPath\": \"" << EscapeJson(processPath) << L"\",\n"
        << L"  \"architecture\": \"Unknown\",\n"
        << L"  \"capturedAtUtc\": \"" << UtcNowIso8601() << L"\",\n"
        << L"  \"captureReason\": \"Manual\",\n"
        << L"  \"exceptionCode\": null,\n"
        << L"  \"dumpType\": \"" << (dumpType == L"full" ? L"FullDump" : L"MiniDump") << L"\",\n"
        << L"  \"dumpFilePath\": \"" << EscapeJson(dumpPath.wstring()) << L"\",\n"
        << L"  \"machineName\": \"" << EscapeJson(GetMachineName()) << L"\",\n"
        << L"  \"osVersion\": \"Windows\",\n"
        << L"  \"doctorDumpVersion\": \"0.1.0\"\n"
        << L"}\n";

    std::wcout << L"Captured dump: " << dumpPath.wstring() << L"\n";
    return 0;
}

void PrintUsage()
{
    std::wcout
        << L"DoctorDump.Agent\n\n"
        << L"Usage:\n"
        << L"  DoctorDump.Agent.exe list [--json]\n"
        << L"  DoctorDump.Agent.exe capture --pid 1234 --type mini --output C:\\Dumps\n";
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

    PrintUsage();
    return 2;
}
