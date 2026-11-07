#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <strsafe.h>

static int Fail(DWORD error)
{
    WCHAR detail[512] = L"";
    WCHAR message[768];
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL,
        error, 0, detail, ARRAYSIZE(detail), NULL);
    StringCchPrintfW(message, ARRAYSIZE(message),
        L"AETHER-Bildschirmschoner konnte nicht gestartet werden (Fehler %lu).\n%s\n"
        L"Bitte AETHER mit der Option Bildschirmschoner erneut installieren.", error, detail);
    MessageBoxW(NULL, message, L"AETHER / Bildschirmschoner", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    return 1;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR arguments, int show)
{
    WCHAR directory[32768], target[32768], command[32768];
    DWORD size = sizeof(directory);
    DWORD length = GetModuleFileNameW(NULL, directory, ARRAYSIZE(directory));
    DWORD attributes;
    PROCESS_INFORMATION process = { 0 };
    STARTUPINFOW startup = { sizeof(startup) };
    DWORD result = 1;
    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(previous);
    UNREFERENCED_PARAMETER(show);
    if (!length || length >= ARRAYSIZE(directory)) return Fail(ERROR_INSUFFICIENT_BUFFER);
    while (length && directory[length - 1] != L'\\') length--;
    directory[length] = 0;
    if (FAILED(StringCchPrintfW(target, ARRAYSIZE(target), L"%sAetherMainboard.scr", directory)))
        return Fail(ERROR_INSUFFICIENT_BUFFER);
    attributes = GetFileAttributesW(target);
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY))
    {
        LSTATUS status = RegGetValueW(HKEY_LOCAL_MACHINE, L"Software\\AetherCockpit", L"InstallPath",
            RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, NULL, directory, &size);
        if (status != ERROR_SUCCESS) return Fail((DWORD)status);
        if (directory[0] == 0 || wcschr(directory, L'"') != NULL ||
            FAILED(StringCchPrintfW(target, ARRAYSIZE(target), L"%s\\AetherMainboard.scr", directory)))
            return Fail(ERROR_INVALID_DATA);
    }
    if (FAILED(StringCchPrintfW(command, ARRAYSIZE(command), L"\"%s\" %s", target, arguments)))
        return Fail(ERROR_INSUFFICIENT_BUFFER);
    if (!CreateProcessW(target, command, NULL, NULL, FALSE, 0, NULL, directory, &startup, &process))
        return Fail(GetLastError());
    CloseHandle(process.hThread);
    if (WaitForSingleObject(process.hProcess, INFINITE) == WAIT_FAILED)
    {
        DWORD error = GetLastError();
        CloseHandle(process.hProcess);
        return Fail(error);
    }
    if (!GetExitCodeProcess(process.hProcess, &result))
    {
        DWORD error = GetLastError();
        CloseHandle(process.hProcess);
        return Fail(error);
    }
    CloseHandle(process.hProcess);
    return (int)result;
}
