/***************************************************************
 * Name:      cl_Client.cpp
 * Purpose:   Code for DNCors client
 **************************************************************/

#include "wx_pch.h"

#include "cl_Client.h"
#include "cl_104_Client.h"

#include "main.h"
#include "Log.h"

wxDEFINE_EVENT(wxEVT_Client_Closed, wxThreadEvent);

// cl_Client
cl_Client::cl_Client(cl_MainApp *pParent, wxSocketBase *pSocket)
	: m_pParent(pParent), m_pSocket(pSocket), m_Client_Rx(this), m_pClient_Rx_Thread(nullptr)
{
	m_itElements = m_Elements.end();
	m_pRxBuffer = new uint8_t[RX_BUFF_LEN];
	m_Client_Rx.m_Sockfd = m_pSocket->GetSocket();
	m_pClient_Rx_Thread = new std::thread (&cl_Client_Rx::Run, &m_Client_Rx);
}

cl_Client::~cl_Client()
{
	if (m_Client_Rx.m_bRunning)
		m_Client_Rx.m_bStop = true;
	if (m_pClient_Rx_Thread != nullptr)
	{
		m_pClient_Rx_Thread->join();
		delete m_pClient_Rx_Thread;
		m_pClient_Rx_Thread = nullptr;
	}
	m_pSocket->Destroy();
	delete[](m_pRxBuffer);
}

bool cl_Client::Setup_104(cl_Init_Cmd *pCmd)
{
	m_sz104Server = pCmd->m_sz104_Server;
	m_p104Client = m_pParent->Find104Client(m_sz104Server);
	if (m_p104Client == nullptr)
		return false;
	m_p104Client->ClientConnected(this);
	return true;
}

uint64_t cl_Client::Find_Element_ID(cl_104_Element *pElement)
{
	auto elem_iter = m_Element_IDs.find(pElement);
	if (elem_iter == m_Element_IDs.end())
		return 0;
	uint64_t nID = elem_iter->second;
	return nID;
}

uint64_t cl_Client::Find_CmdElement_ID(cl_104_Element *pElement)
{
	auto elem_iter = m_CmdElement_IDs.find(pElement);
	if (elem_iter == m_CmdElement_IDs.end())
		return 0;
	uint64_t nID = elem_iter->second;
	return nID;
}


// cl_Client_Rx
cl_Client_Rx::cl_Client_Rx(cl_Client *pParent)
	: m_pParent(pParent), m_bRunning(false), m_bStop(false), m_Sockfd(-1)
{}

cl_Client_Rx::~cl_Client_Rx()
{}

//PM - prijem dat, osetreni cteni po castech - vrati true, pokud se podarilo precist pozadovany pocet bytu
bool cl_Client_Rx::SockRcv(uint8_t *pBuffer, int nBytes)
{
	int nRet;
	int nOffset = 0;
	while (nBytes > 0)
	{
		nRet = recv(m_Sockfd, (char*)(pBuffer + nOffset), nBytes, 0);
		if (nRet < 0)
		{return false;}
		else if (nRet == 0)
		{return false;	}
		nBytes -= nRet;
		nOffset += nRet;
	}
	return true;
}

void cl_Client_Rx::Run()
{
	WORD wVersionRequested;
	WSADATA wsaData;
	int err;

	wVersionRequested = MAKEWORD(2, 2);

	err = WSAStartup(wVersionRequested, &wsaData);
	if (err != 0)
	{
		return;
	}
	m_bRunning = true;
	std::chrono::milliseconds connect_to(200);
	while (true)
	{
		fd_set readfds;
		do
		{
			FD_ZERO(&readfds);
			FD_SET(m_Sockfd, &readfds);
			struct timeval to = {.tv_sec = 0, .tv_usec = 100000};
			int rc = select(m_Sockfd + 1, &readfds, nullptr, nullptr, &to);
			if (rc == -1)
			{goto Rx_exit;}

			if (m_bStop)
				goto Rx_exit;
		} while (!FD_ISSET(m_Sockfd, &readfds));

		if (!SockRcv(m_pParent->m_pRxBuffer, sizeof(cl_Serializer::tlv_head_t)))
		{
			goto Rx_exit;
		}

		do
		{
			FD_ZERO(&readfds);
			FD_SET(m_Sockfd, &readfds);
			struct timeval to = {.tv_sec = 0, .tv_usec = 100000};
			int rc = select(m_Sockfd + 1, &readfds, nullptr, nullptr, &to);
			if (rc == -1)
			{goto Rx_exit;}

			if (m_bStop)
				goto Rx_exit;
		} while (!FD_ISSET(m_Sockfd, &readfds));

		if (!SockRcv(m_pParent->m_pRxBuffer + sizeof(cl_Serializer::tlv_head_t), ((cl_Serializer::tlv_head_t*)m_pParent->m_pRxBuffer)->nLength))
		{
			goto Rx_exit;
		}

		//PM - zpracovani kompletni prijate zpravy
		cl_Serializer TLV_Rx;
		cl_Command *pRxCmd = nullptr;
		try
		{
			TLV_Rx.ReadTLV(nullptr, m_pParent->m_pRxBuffer, RX_BUFF_LEN);
			pRxCmd = dynamic_cast <cl_Command*> (TLV_Rx.Deserialize());
		}
		catch (cl_BaseException *e)
		{
			delete e;
			goto Rx_exit;
		}

		if (pRxCmd == nullptr)
		{goto Rx_exit;}

		pRxCmd->Exec(m_pParent);

		if (pRxCmd->m_uAnswer.get() != nullptr)
		{
			if (!Send(pRxCmd->m_uAnswer.get()))
			{goto Rx_exit;}
		}
		delete pRxCmd;
	}

Rx_exit:
	m_Sockfd = -1;
	WSACleanup();
	m_bRunning = false;
	wxThreadEvent *CloseEvent = new wxThreadEvent(wxEVT_Client_Closed);
	CloseEvent->SetEventObject((wxObject *)m_pParent);
	wxTheApp->QueueEvent(CloseEvent);
}

bool cl_Client_Rx::Send(cl_Command *pCmd)
{
	cl_Serializer TLV;
	TLV.Serialize(pCmd);
	uint8_t *pTx_Buff = nullptr;
	uint32_t Cmd_len = TLV.WriteTLVArchive(&pTx_Buff, 0);
	int nRes = send(m_Sockfd, (const char*)pTx_Buff, Cmd_len, 0);
	free(pTx_Buff);
	return (nRes >= 0);
}






