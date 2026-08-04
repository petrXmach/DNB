/***************************************************************
 * Name:      main.cpp
 * Purpose:   Code for main of DNCors_IEC104
 **************************************************************/

#include "wx_pch.h"

#include <wx/stdpaths.h>
#include <wx/wx.h>
#include <wx/dir.h>

#include <fstream>

#include "main.h"
#include "Log.h"

#define wxLOG_COMPONENT		"main/main"

TCHAR serviceName[32]= TEXT("DNCors_IEC104");

IMPLEMENT_APP_NO_MAIN(cl_MainApp)

HINSTANCE g_hInstance;
HINSTANCE g_hPrevInstance;
wxCmdLineArgType g_lpCmdLine;
int g_nCmdShow;

void WINAPI ServiceMain(DWORD argc, LPTSTR *argv);
void WINAPI ServiceCtrlHandler(DWORD opcode);

SERVICE_TABLE_ENTRY  ServiceTable[] =
{
    { serviceName, (LPSERVICE_MAIN_FUNCTION) ServiceMain },
    { NULL, NULL }
};

SERVICE_STATUS cl_MainApp::g_ServiceStatus = {0};
SERVICE_STATUS_HANDLE cl_MainApp::g_StatusHandle;

const long cl_MainApp::SERVER_ID = wxNewId();
const long cl_MainApp::SOCKET_ID = wxNewId();

cl_MainApp *g_pMain = nullptr;

extern "C" int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, wxCmdLineArgType lpCmdLine, int nCmdShow)
{
	wxDISABLE_DEBUG_SUPPORT();
	if(strstr(lpCmdLine, "--svc") == nullptr)     // didn't find it
	{
		return wxEntry(hInstance, hPrevInstance, lpCmdLine, nCmdShow);  // calls OnInit()
	}
	else 
	{
		g_hInstance = hInstance;
		g_hPrevInstance = hPrevInstance;
		g_lpCmdLine = lpCmdLine;
		g_nCmdShow = nCmdShow;
		if(!StartServiceCtrlDispatcher(ServiceTable))
		{
			return GetLastError();
		}
	}
	return 0;
}

// cl_MainApp
cl_MainApp::cl_MainApp()
	: m_Connect_104Srv(this), m_SCM(nullptr)
{
	g_pMain = this;
}

bool cl_MainApp::OnInit()
{
	wxString exePath = wxStandardPaths::Get().GetExecutablePath().BeforeLast('\\');
	if (!m_Config.Open(exePath))
	{return false;}

	if (!m_Config.Read())
	{return false;}

		SetExitOnFrameDelete(false);

		// Create the address - defaults to localhost:0 initially
		wxIPV4address addr;
		addr.Service(TCP_COMM_PORT);

		// Create the socket
		m_uServer = std::make_unique <wxSocketServer>(addr, wxSOCKET_NOWAIT);

		if (!m_uServer->Ok())
		{
			return false;
		}
		else
		{
			// Setup the event handler and subscribe to connection events
			m_uServer->SetEventHandler(*this, SERVER_ID);
			m_uServer->SetNotify(wxSOCKET_CONNECTION_FLAG);
			m_uServer->Notify(true);
			//PM prijem prichozich spojeni od klientu, prirazeni socketu	
			Bind(wxEVT_SOCKET, &cl_MainApp::OnServerEvent, this, SERVER_ID);//TLV communication	
			Bind(wxEVT_Client_Closed, &cl_MainApp::Client_Closed, this);
		}

		if (!Init_Servers())//IEC104 server communication
		{
			return false;
		}

		g_ServiceStatus.dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN;
		g_ServiceStatus.dwCurrentState = SERVICE_RUNNING;
		g_ServiceStatus.dwWin32ExitCode = 0;
		g_ServiceStatus.dwCheckPoint = 0;

		return true;
}

//PM prijem prichozich spojeni od klientu, prirazeni socketu, TLV communication
void cl_MainApp::OnServerEvent(wxSocketEvent& event)
{
	wxSocketBase *sock;
	if (event.GetSocketEvent() != wxSOCKET_CONNECTION)
	{
		return;
	}
	sock = m_uServer->Accept(false);
	if (sock == nullptr)
	{return;}
	cl_Client_UPtr uClient = std::make_unique <cl_Client>(this, sock);
	m_Clients.push_back(std::move(uClient));
}


void cl_MainApp::RemoveClient(cl_Client *pClient)
{
	for (auto iter = m_Clients.begin(); iter != m_Clients.end(); ++iter)
	{
		if ((*iter).get() == pClient)
		{
			m_Clients.erase(iter);
			break;
		}
	}
}

void cl_MainApp::Client_Closed(wxThreadEvent &event)
{
	wxLogDebug("cl_MainApp::Client_Closed");
	cl_Client *pClient = (cl_Client*)event.GetEventObject();
	if (!m_Config.m_bReplayMode)
	{
		if ((pClient != nullptr) && (pClient->m_p104Client != nullptr))
		{
			if (pClient->m_p104Client->m_pStatus_Element != nullptr)
			{
				pClient->m_p104Client->m_pStatus_Element->m_fValue = 0.;
				pClient->m_p104Client->SinglePointInformation(pClient->m_p104Client->m_pStatus_Element, false, CS101_COT_ACTIVATION, false);
			}
		}
	}
	RemoveClient(pClient);
}

int cl_MainApp::OnExit()
{
	return 0;
}

cl_104_Client *cl_MainApp::Find104Client(wxString szName)
{
	cl_104_Client *p104Client = nullptr;
	for (auto iter = m_104_Clients.begin(); iter != m_104_Clients.end(); ++iter)
	{
		p104Client = (*iter).get();
		if (p104Client->m_szName == szName)
			return p104Client;
	}
	return nullptr;
}

bool cl_MainApp::Init_Servers()
{
	wxString szExePath = wxStandardPaths::Get().GetExecutablePath().BeforeLast('\\');
	szExePath += "/Server/";
	szExePath.Replace("\\", "/");
	wxDir dir(szExePath);
	if (!dir.IsOpened())
		 return false;

	bool bResult = true;

	wxString szDirName, szFileName;
	bool cont = dir.GetFirst(&szDirName, wxEmptyString, wxDIR_DIRS);
	while (cont)
	{
		if (wxFile::Exists(szExePath + szDirName + "/IP.txt"))
		{
			cl_104_Client_UPtr uClient = std::make_unique<cl_104_Client> (this, szDirName);
			if (!uClient->GetIP(szExePath + szDirName + "/IP.txt"))
			{
				bResult = false;
				continue;
			}

			if (wxFile::Exists(szExePath + szDirName + "/XChng.cfg"))
				uClient->GetXChngCfg(szExePath + szDirName + "/XChng.cfg");

			m_104_Clients.push_back(std::move(uClient));
		}
		cont = dir.GetNext(&szDirName);
	}

	if (!m_Config.m_bReplayMode)
	{
		m_u104_Connect_Thrd = std::make_unique<std::thread> (&cl_ConnectServer::Run, &m_Connect_104Srv);
	}
	else
	{
		for (auto iter = m_104_Clients.begin(); iter != m_104_Clients.end(); ++iter)
		{
			cl_104_Client *pClient = (*iter).get();
			bResult &= pClient->OpenReplay(m_Config.m_szReplay_Dir);
		}
	}
	return bResult;
}

void WINAPI ServiceMain(DWORD argc, LPTSTR *argv)
{
    wxLogDebug("ServiceMain: Start");

    cl_MainApp::g_StatusHandle = RegisterServiceCtrlHandler(serviceName, &ServiceCtrlHandler);
    if(!cl_MainApp::g_StatusHandle)
    {
        return;
    }

    cl_MainApp::g_ServiceStatus.dwServiceType        = SERVICE_WIN32_OWN_PROCESS;
    cl_MainApp::g_ServiceStatus.dwControlsAccepted   = 0;
    cl_MainApp::g_ServiceStatus.dwCurrentState       = SERVICE_START_PENDING;
    cl_MainApp::g_ServiceStatus.dwWin32ExitCode      = NO_ERROR;  // (0)
    cl_MainApp::g_ServiceStatus.dwServiceSpecificExitCode = 0;
    cl_MainApp::g_ServiceStatus.dwCheckPoint         = 0;
    cl_MainApp::g_ServiceStatus.dwWaitHint           = 0;
    if(SetServiceStatus(cl_MainApp::g_StatusHandle, &cl_MainApp::g_ServiceStatus))
    {
        wxEntry(g_hInstance, g_hPrevInstance, g_lpCmdLine, g_nCmdShow);  // This calls OnInit()   // should this be wxEntryStart() ?
    }
    return;
}

// cl_Config
cl_Config::cl_Config()
	: m_bOK(false), m_nLogLevel(2), m_bLogData(false), m_nASDU_Debug(0)
{
	m_bReplayMode = false;
	m_bReplayMap = false;
	m_bReplayBuff = true;
}

bool cl_Config::Open(wxString &szPath)
{
	wxString szINI_File = szPath + "/" + CONFIG_FILE_NAME;
	if (!wxFileExists(szINI_File))
	{return (m_bOK = false);}

	m_uConfig = std::make_unique<wxFileConfig>(APP_NAME, COMPANY_NAME, szINI_File, wxEmptyString, wxCONFIG_USE_RELATIVE_PATH | wxCONFIG_USE_LOCAL_FILE | wxCONFIG_USE_NO_ESCAPE_CHARACTERS);
	m_uConfig->DontCreateOnDemand();

	if (!m_uConfig->Read(wxT("/Config/Log_File"), &m_szLog_File))
	{return false;}

	return true;
}

bool cl_Config::Read()
{
	long nTmp;
	wxString szTmp;

	if (m_uConfig->Read(wxT("/Config/Log_Level"), &nTmp))
	{
		m_nLogLevel = nTmp;
		set_log_level(m_nLogLevel);
	}

	if (m_uConfig->Read(wxT("/Data/Log"), &nTmp))
		m_bLogData = (nTmp == 1);
	if (!m_uConfig->Read(wxT("/Data/Dir"), &m_szData_Dir))
		m_bLogData = false;
	if (m_uConfig->Read(wxT("/Config/ReplayMode"), &nTmp))
		m_bReplayMode = (nTmp == 1);

	if (m_uConfig->Read(wxT("/Config/ASDU_Dbg"), &nTmp))
		m_nASDU_Debug = nTmp;
	m_uConfig.reset();
	m_bOK = true;
	return true;
}