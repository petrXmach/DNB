#ifndef DNCors_IEC104MAIN_H
#define DNCors_IEC104MAIN_H

#include "wx/socket.h"

#include <inttypes.h>
#include <memory>
#include <list>
#include <thread>

#include "common.h"

#include "cs104_connection.h"

#include "cl_Client.h"
#include "cl_104_Client.h"

// cl_Config
class cl_Config
{
public:
	bool											m_bOK;
	std::unique_ptr<wxFileConfig> 		m_uConfig;

	wxString										m_szLog_File;
	int											m_nLogLevel;

	bool											m_bLogData;
	wxString										m_szData_Dir;

//	uint64_t										m_n104Mask;
//	int											m_nType_Shift;

	// Replay - data simulation - mode
	bool											m_bReplayMode;
	wxString										m_szReplay_Dir;
	bool											m_bReplayMap;
	bool											m_bReplayBuff;

	int											m_nASDU_Debug;

public:
	cl_Config ();
	bool Open(wxString &szPath);
	bool Read();
	bool IsOK() { return m_bOK; }
};

class cl_MainApp;

//----------------------------------------------------------------------------
// cl_MainApp
//----------------------------------------------------------------------------
class cl_MainApp: public wxApp
{
public:
	cl_Config									m_Config;

	static SERVICE_STATUS g_ServiceStatus;
	static SERVICE_STATUS_HANDLE g_StatusHandle;
	std::unique_ptr<wxSocketServer> 		m_uServer;
	std::list<cl_Client_UPtr>				m_Clients;
	std::list<cl_104_Client_UPtr>			m_104_Clients;
	std::unique_ptr<std::thread>			m_u104_Connect_Thrd;
	cl_ConnectServer							m_Connect_104Srv;
private:
	SC_HANDLE 									m_SCM;

public:
	static const long 						SERVER_ID;
	static const long 						SOCKET_ID;

public:
	cl_MainApp();
	virtual bool OnInit();
	virtual int OnExit();

	void OnServerEvent(wxSocketEvent& event);
	void Client_Closed(wxThreadEvent &event);
	cl_104_Client *Find104Client(wxString szName);
	void RemoveClient(cl_Client *pClient);
	bool Init_Servers();
};

extern cl_MainApp *g_pMain;

#endif		//DNCors_IEC104MAIN_H

