// "Apply with NetHop" in the Windows 11 context menu of .nethop files.
//
// Windows 11 shows first-level menu entries only for IExplorerCommand handlers declared by a package.
// NetHop.ContextMenu.msix (see AppxManifest.xml) is a sparse package whose content is the install folder;
// it declares this class for .nethop files. Explorer loads this DLL in-process, so it stays small and native:
// it only starts NetHop.App.exe --apply <file>, like the classic "Apply with NetHop" verb.

#include <windows.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <string>

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::InProc;
using Microsoft::WRL::Module;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

namespace
{
    HMODULE g_module = nullptr;

    // Folder of this DLL, which is NetHop's install folder.
    std::wstring ModuleFolder()
    {
        std::wstring path(MAX_PATH, L'\0');
        for (;;)
        {
            const DWORD length = GetModuleFileNameW(g_module, path.data(), static_cast<DWORD>(path.size()));
            if (length == 0)
                return {};
            if (length < path.size())
            {
                path.resize(length);
                break;
            }
            path.resize(path.size() * 2);
        }
        return path.substr(0, path.find_last_of(L'\\'));
    }

    std::wstring AppPath()
    {
        return ModuleFolder() + L"\\NetHop.App.exe";
    }

    // Same rule as the app: the language picked in NetHop's settings (a tag such as "fr-FR"),
    // otherwise Windows' display language.
    WORD PrimaryLanguage()
    {
        wchar_t tag[LOCALE_NAME_MAX_LENGTH] = {};
        DWORD size = sizeof(tag);
        if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\NetHop", L"Language", RRF_RT_REG_SZ, nullptr, tag, &size) == ERROR_SUCCESS
            && tag[0] != L'\0')
        {
            const LCID locale = LocaleNameToLCID(tag, 0);
            if (locale != 0)
                return PRIMARYLANGID(LANGIDFROMLCID(locale));
        }
        return PRIMARYLANGID(GetUserDefaultUILanguage());
    }

    // The languages of the app (src/NetHop.App/Strings); English otherwise.
    const wchar_t* Title()
    {
        switch (PrimaryLanguage())
        {
        case LANG_GERMAN: return L"Mit NetHop anwenden";
        case LANG_SPANISH: return L"Aplicar con NetHop";
        case LANG_FRENCH: return L"Appliquer avec NetHop";
        case LANG_ITALIAN: return L"Applica con NetHop";
        case LANG_PORTUGUESE: return L"Aplicar com o NetHop";
        default: return L"Apply with NetHop";
        }
    }
}

class __declspec(uuid("d865f53b-c891-4bc6-86fc-31839648b395")) ApplyCommand final
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return SHStrDupW(Title(), name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        const std::wstring location = AppPath() + L",0";
        return SHStrDupW(location.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        *tip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* name) override
    {
        *name = __uuidof(ApplyCommand);
        return S_OK;
    }

    // One profile at a time: hidden when several files are selected.
    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        DWORD count = 0;
        if (items != nullptr)
            items->GetCount(&count);
        *state = count == 1 ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (items == nullptr)
            return E_INVALIDARG;

        ComPtr<IShellItem> item;
        HRESULT hr = items->GetItemAt(0, &item);
        if (FAILED(hr))
            return hr;

        PWSTR file = nullptr;
        hr = item->GetDisplayName(SIGDN_FILESYSPATH, &file);
        if (FAILED(hr))
            return hr;
        std::wstring commandLine = L"\"" + AppPath() + L"\" --apply \"" + file + L"\"";
        CoTaskMemFree(file);

        // NetHop runs as a single instance: a running NetHop receives the request and this process exits.
        const std::wstring folder = ModuleFolder();
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, folder.c_str(), &startup, &process))
            return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        *commands = nullptr;
        return E_NOTIMPL;
    }
};

CoCreatableClass(ApplyCommand)

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** factory)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, iid, factory);
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
