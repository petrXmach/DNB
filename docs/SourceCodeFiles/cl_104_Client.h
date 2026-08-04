/***************************************************************
 * Name:      cl_104_Client.h
 * Purpose:   Defines DNCors IEC104 client
 **************************************************************/

#ifndef CL_104_CLIENT_H
#define CL_104_CLIENT_H

#include <inttypes.h>
#include <memory>
#include <list>
#include <map>
#include <deque>

#include <wx/file.h>
#include <wx/socket.h>
#include <wx/thread.h>
#include <wx/textfile.h>

#include "common.h"

#include "cs104_connection.h"
#include "Commands.h"

#define DATA_TYPE_INT		1
#define DATA_TYPE_BOOL		2
#define DATA_TYPE_FLOAT		3

class cl_104_Client;

// cl_104_Data
class cl_104_Data
{
public:
	int								m_nCA;
	int								m_nIOA;
	int								m_nCOT;
	int								m_nType;
	QualityDescriptor				m_nQualDesc;

	bool 								m_bTimeSet;
	wxDateTime						m_dtTime;

	long								m_nListPosition;

public:
	cl_104_Data(CS101_ASDU ASDU, int nIx);
	virtual ~cl_104_Data();

	int GetCA() { return m_nCA; }
	int GetIOA() { return m_nIOA; }
	int GetCOT() { return m_nCOT; }
	wxString GetStrType();
	void SetCOT(CS101_ASDU ASDU);
	wxString GetStrTime();
	wxDateTime GetTime();

	void SetTime(InformationObject IO);

	virtual wxString GetStrTypeHuman() = 0;
	virtual wxString GetStrValue() = 0;
	virtual wxString GetStrQual() { return wxString::Format("0x%02X", m_nQualDesc); }

	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) = 0;

	virtual double GetFltValue() = 0;

	virtual int GetDataType() = 0;
	int GetValType();

	static cl_104_Data *Create_from_ASDU(CS101_ASDU ASDU, int nIx);
};

typedef std::unique_ptr<cl_104_Data> cl_104_Data_UPtr;

// cl_104_Meas_Int
class cl_104_Meas_Int : public cl_104_Data
{
public:
	int								m_nValue;
public:
	cl_104_Meas_Int(CS101_ASDU ASDU, int nIx);

	virtual wxString GetStrValue() override;
	virtual wxString GetStrTypeHuman() override { return "Measured scaled value"; }
	virtual int GetDataType() override { return DATA_TYPE_INT; }
	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) override;
	virtual double GetFltValue() override { return m_nValue; }
};

// cl_104_Single_Info
class cl_104_Single_Info : public cl_104_Data
{
public:
	bool							m_bValue;

public:
	cl_104_Single_Info(CS101_ASDU ASDU, int nIx);

	virtual wxString GetStrValue() override;
	virtual wxString GetStrTypeHuman() override { return "Single point of information"; }
	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) override;
	virtual int GetDataType() override { return DATA_TYPE_BOOL; }
	virtual double GetFltValue() override { return m_bValue ? 1. : 0.; }
};

// cl_104_Double_Info
class cl_104_Double_Info : public cl_104_Data
{
public:
	int							m_nValue;

public:
	cl_104_Double_Info(CS101_ASDU ASDU, int nIx);

	virtual wxString GetStrValue() override;
	virtual wxString GetStrTypeHuman() override { return "Double point of information"; }
	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) override;
	virtual int GetDataType() override { return DATA_TYPE_INT; }
	virtual double GetFltValue() override { return m_nValue; }
};

// cl_104_Meas_Float
class cl_104_Meas_Float : public cl_104_Data
{
public:
	float							m_fValue;

public:
	cl_104_Meas_Float(CS101_ASDU ASDU, int nIx);

	virtual wxString GetStrValue() override;
	virtual wxString GetStrTypeHuman() override { return "Measured float value"; }
	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) override;
	virtual int GetDataType() override { return DATA_TYPE_FLOAT; }
	virtual double GetFltValue() override { return m_fValue; }
};

// cl_104_SetPoint_Float
class cl_104_SetPoint_Float : public cl_104_Data
{
public:
	float							m_fValue;

public:
	cl_104_SetPoint_Float(CS101_ASDU ASDU, int nIx);

	virtual wxString GetStrValue() override;
	virtual wxString GetStrTypeHuman() override { return "SetPoint float value"; }
	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) override;
	virtual int GetDataType() override { return DATA_TYPE_FLOAT; }
	virtual double GetFltValue() override { return m_fValue; }
};

// cl_104_Step_Pos_Info
class cl_104_Step_Pos_Info : public cl_104_Data
{
public:
	int								m_nValue;

public:
	cl_104_Step_Pos_Info(CS101_ASDU ASDU, int nIx);

	virtual wxString GetStrValue() override;
	virtual wxString GetStrTypeHuman() override { return "Step position information"; }
	virtual int GetDataType() override { return DATA_TYPE_INT; }
	virtual void SetValue(InformationObject IO, CS101_ASDU ASDU) override;
	virtual double GetFltValue() override { return m_nValue; }
};

#define NO_ELEM_ID	0xFFFFFFFFFFFFFFFF

#define Val_GEN_U		15
#define Val_GEN_P		22
#define Val_GEN_Q		23
#define Val_GEN_IL2	2

#define Val_AIO_L1_L3	18

#define Val_SW_OdpQ1	12
#define Val_SW_OdpQ6	17
#define Val_SW_OdpQ11	23
#define Val_SW_OdpQ12	24
#define Val_SW_Odpi	124
#define Val_SW_Vyp	1

#define Val_UL1s1		82
#define Val_UL2s1		83
#define Val_UL3s1		84
#define Val_Uss1		86
#define Val_UL1s2		87
#define Val_UL2s2		88
#define Val_UL3s2		89
#define Val_Uss2		91

#define Dbl_Info_FALSE	1
#define Dbl_Info_TRUE	2

#define ElemTYPE_NONE	0
#define ElemTYPE_SW		1
#define ElemTYPE_GEN		2
#define ElemTYPE_MEAS	3
#define ElemTYPE_NODE	4
#define ElemTYPE_BRANCH	5

// cl_104_Element
class cl_104_Element
{
public:
	cl_104_Client 								*m_pClient;
	uint64_t										m_nAddress;
	uint64_t										m_nACK_Address;
	bool											m_bPropagate;
	bool											m_bSetPoint;
	uint64_t										m_nCtrl_ID;
	wxFile										m_FileTxt;

	wxDateTime									m_dtLastData;
	double										m_fValue;
	uint32_t										m_nQuality;
	uint8_t										m_nType;

	// Replay variables
	wxTextFile 									m_ReplayFile;
	wxDateTime									m_dtDataFrom;
	wxDateTime									m_dtDataTo;

	std::map<uint64_t, cl_Replay_Data_UPtr> 	m_mapReplay;

public:
	cl_104_Element(cl_104_Client *pClient, uint64_t nAddress, bool bIsParameter);
	~cl_104_Element();

	void LogValue(cl_104_Data *pData);

	virtual void NewData(cl_104_Data *pData);
	virtual bool HaveNewData(wxDateTime dtNewerThan);

	virtual cl_Elem_104_Value *GetData();

	uint32_t GetIOA();
	uint32_t GetCA();

	static uint32_t GetIOAPart(uint64_t nAddress);
	static uint32_t GetCAPart(uint64_t nAddress);

	static bool StrToAddr(wxString szStr, uint64_t &nValue, bool bHex);
	static wxString AddrToStr(uint64_t nAddress);
	static int AddrToValType(uint64_t nAddr);

	bool OpenReplay(wxString szPath);
	bool InitReplay();
	bool ReplayMove(wxDateTime &dtTime);

	virtual void SetLogData(cl_Replay_Data *pLine);
	virtual int GetType() { return ElemTYPE_NONE; }
};

typedef std::unique_ptr<cl_104_Element> cl_104_Element_UPtr;

class cl_MainApp;
class cl_Client;
class cl_104_Client;

// cl_104_Client
class cl_104_Client
{
public:
	cl_MainApp									*m_pParent;
	CS104_Connection 							m_104_Connection;
	wxString										m_szName;
	bool											m_bConnected;
	bool											m_bDisConnect;
	std::vector<wxIPV4address>				m_Server_Addr;
	int											m_nAct_Srv_Index;
	std::map<uint64_t, cl_104_Element_UPtr> 	m_Elements;
	std::list<cl_104_Element*> 			m_Interrog_Elements;
	cl_104_Element*							m_pStatus_Element;
	// list of connected clients - not owning, just connected. Owned by cl_MainApp class
	std::list<cl_Client*>					m_Clients;

public:
	cl_104_Client(cl_MainApp *pParent, wxString szName);
	~cl_104_Client();

	bool Connect();
	void CloseConnection();
	void NewData(cl_104_Data *pData);
	void SetNextServer();

	void ClientConnected(cl_Client *pClient);
	void ClientClosed(cl_Client *pClient);

	bool GetIP(wxString szFile);
	bool GetXChngCfg(wxString szFile);

	cl_104_Element *FindElement(uint64_t nAddress, bool bAutoInsert = true, bool bParameter = false);
	cl_104_Element *FindElementName(wxString szName);

	static void ConnectionHandler(void* parameter, CS104_Connection connection, CS104_ConnectionEvent event);
	static bool ASDU_ReceivedHandler (void* parameter, int address, CS101_ASDU asdu);

	bool OpenReplay(wxString szPath);

	bool Rx_Interrogation(CS101_ASDU asdu);
	void AddInterrog(cl_104_Element *pElem);

	void Send_MeasuredValueShort(cl_104_Element *pElem, double fValue, uint8_t nCOT, bool bAckAddr);
	void Send_SetPoint_ShrtFP(cl_104_Element *pElem, double fValue, uint8_t nCOT, bool bAckAddr);
	void SinglePointInformation(cl_104_Element *pElem, bool bValue, uint8_t nCOT, bool bAckAddr);
	void Send_SingleCommand(cl_104_Element *pElem, bool bValue, uint8_t nCOT, bool bAckAddr);
	void DoublePointInformation(cl_104_Element *pElem, int nValue, uint8_t nCOT, bool bAckAddr);
	void Send_DoubleCommand(cl_104_Element *pElem, int nValue, uint8_t nCOT, bool bAckAddr);
};

typedef std::unique_ptr<cl_104_Client> cl_104_Client_UPtr;

// cl_ConnectServer
class cl_ConnectServer
{
public:
	cl_MainApp					*m_pParent;
	bool							m_bStop;
	wxSemaphore					m_Semaphore;

public:
	cl_ConnectServer(cl_MainApp *pParent);
	void Run();
};

#endif			// CL_104_CLIENT_H


