/***************************************************************
 * Name:      cl_Client.h
 * Purpose:   Defines DNCors client
 * Author:    EGC CB s.r.o. (khojdar@egc-cb.cz)
 * Created:   2021-01-22
 * Copyright: EGC CB s.r.o. (www.egc-cb.cz)
 * License:
 **************************************************************/

#ifndef CL_CLIENT_H
#define CL_CLIENT_H

#include <inttypes.h>
#include <memory>
#include <list>
#include <map>
#include <deque>
#include <thread>

#include "common.h"

#include <wx/wx.h>
#include <wx/log.h>
#include "wx/socket.h"

#include "Commands.h"

#define RX_BUFF_LEN 			128 * 1024

wxDECLARE_EVENT(wxEVT_Client_Closed, wxThreadEvent);

class cl_MainApp;
class cl_104_Client;
class cl_104_Element;
class cl_Client;

// cl_Client_Rx
class cl_Client_Rx
{
public:
	cl_Client									*m_pParent;
	bool											m_bRunning;
	volatile bool								m_bStop;
	int											m_Sockfd;

	wxCriticalSection							m_csCmdLst;
	std::deque<cl_Command *>				m_lstCommand;

public:
	cl_Client_Rx(cl_Client *pParent);
	virtual ~cl_Client_Rx();
	void Run();

	bool SockRcv(uint8_t *pBuffer, int nBytes);

	bool Send(cl_Command *pCmd);
};

// cl_Client
class cl_Client : public cl_Cmd_Dest
{
public:
	cl_MainApp									*m_pParent;
	wxSocketBase 								*m_pSocket;
	uint8_t										*m_pRxBuffer;
	wxString										m_sz104Server;
	cl_104_Client 								*m_p104Client;
	cl_Client_Rx								m_Client_Rx;
	std::thread 								*m_pClient_Rx_Thread;

	// Only link scheme elements to 104_Elements, objects are not owned by this structure
	std::map<uint64_t, cl_104_Element*> 	m_Elements;
	std::map<uint64_t, cl_104_Element*>::iterator		m_itElements;
	std::map<cl_104_Element*, uint64_t> 	m_Element_IDs;
	std::map<uint64_t, cl_104_Element*> 	m_CmdElements;
	std::map<cl_104_Element*, uint64_t> 	m_CmdElement_IDs;

public:
	cl_Client(cl_MainApp *pParent, wxSocketBase *pSocket);
	~cl_Client();
	bool Setup_104(cl_Init_Cmd *pCmd);
	uint64_t Find_Element_ID(cl_104_Element *pElement);
	uint64_t Find_CmdElement_ID(cl_104_Element *pElement);
};

typedef std::unique_ptr<cl_Client> cl_Client_UPtr;
#endif // CL_CLIENT_H

