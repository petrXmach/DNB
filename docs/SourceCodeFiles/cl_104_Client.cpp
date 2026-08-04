/***************************************************************
 * Name:      cl_104_Client.cpp
 * Purpose:   Code for DNCors cl_104_Client
 **************************************************************/

#include "wx_pch.h"

#include <wx/tokenzr.h>
#include <wx/dir.h>

#include "cl_104_Client.h"
#include "cl_Client.h"

#include "Log.h"
#include "main.h"

// cl_104_Client
cl_104_Client::cl_104_Client(cl_MainApp *pParent, wxString szName)
	: m_pParent(pParent), m_104_Connection(nullptr), m_szName(szName), m_bConnected(false), m_bDisConnect(false), m_nAct_Srv_Index(0),
	  m_pStatus_Element(nullptr)
{
}

cl_104_Client::~cl_104_Client()
{CloseConnection();}

bool cl_104_Client::Connect()
{
	wxString szIP = m_Server_Addr[m_nAct_Srv_Index].IPAddress();
	int nPort = m_Server_Addr[m_nAct_Srv_Index].Service();
	if (m_104_Connection != nullptr)
		CS104_Connection_destroy(m_104_Connection);
	m_104_Connection = CS104_Connection_create(szIP, nPort);
	CS104_Connection_setConnectionHandler(m_104_Connection, ConnectionHandler, this);
	CS104_Connection_setASDUReceivedHandler(m_104_Connection, ASDU_ReceivedHandler, this);

	m_bConnected = CS104_Connection_connect(m_104_Connection);

	if (m_bConnected)
	{
		wxLogDebug("  --Connected - success");
	}
	else
	{
		wxLogDebug("  --Not connected - failure");
	}
	return m_bConnected;
}

void cl_104_Client::CloseConnection()
{
	if (!m_bConnected)
		return;

	m_bDisConnect = true;
	m_pParent->m_Connect_104Srv.m_Semaphore.Post();
	return;
}

void cl_104_Client::SetNextServer()
{
	m_nAct_Srv_Index++;
	if (m_nAct_Srv_Index >= (int)m_Server_Addr.size())
		m_nAct_Srv_Index = 0;
}

bool cl_104_Client::GetIP(wxString szFile)
{
	bool bResult = true;
	wxTextFile IPFile(szFile);
	if (!IPFile.Open())
	{
	  return false;
	}

	long nTmp;
	int nLine = 0;
	for (wxString szLine = IPFile.GetFirstLine(); !IPFile.Eof(); szLine = IPFile.GetNextLine())
	{
		nLine++;

		if ((szLine.Left(2) == wxT("//")) || (szLine.Left(1) == wxT("#")))
			continue;
		if (szLine == wxEmptyString)
			continue;

		wxStringTokenizer LineTok(szLine, wxT("\t"), wxTOKEN_STRTOK);
		wxString szIP = LineTok.GetNextToken();
		int nPort = 2404;
		if (szIP.Find(':') != wxNOT_FOUND)
		{
			wxString szPort = szIP.AfterLast(':');
			if (!szPort.ToLong(&nTmp))
			{
				bResult = false;
				continue;
			}
			nPort = nTmp;
			szIP = szIP.BeforeLast(':');
		}

		wxIPV4address Addr;
		if (!Addr.Hostname(szIP) || !Addr.Service(nPort))
		{
			bResult = false;
			continue;
		}

		m_Server_Addr.push_back(Addr);
	}
	return bResult;
}

bool cl_104_Client::GetXChngCfg(wxString szFile)
{
	bool bResult = true;
	wxTextFile IPFile(szFile);
	if (!IPFile.Open())
	{
	  return false;
	}

	int nLine = 0;
	for (wxString szLine = IPFile.GetFirstLine(); !IPFile.Eof(); szLine = IPFile.GetNextLine())
	{
		nLine++;

		if ((szLine.Left(2) == wxT("//")) || (szLine.Left(1) == wxT("#")))
			continue;
		if (szLine == wxEmptyString)
			continue;

		wxStringTokenizer LineTok(szLine, wxT("\t"), wxTOKEN_STRTOK);
		wxString szIEC104_Addr = LineTok.GetNextToken();
		wxString szID;
		if (LineTok.CountTokens() > 0)
			szID = LineTok.GetNextToken();

		if (szID == wxEmptyString)
		{continue;		}

		wxString szType;
		if (LineTok.CountTokens() > 0)
			szType = LineTok.GetNextToken();

		if (szType == wxEmptyString)
		{
			continue;
		}

		uint64_t nAddr;
		unsigned long nID, nType;
		if (!cl_104_Element::StrToAddr(szIEC104_Addr, nAddr, false))
		{
			continue;
		}
		if (!szID.ToCULong(&nID))
		{
			continue;
		}
		if (!szType.ToCULong(&nType))
		{
			continue;
		}
		cl_104_Element *pElem = FindElement(nAddr, true, true);
		pElem->m_nCtrl_ID = nID | MAIN_104_Flag;
		pElem->m_nType = nType;
		m_Interrog_Elements.push_back(pElem);
		if (pElem->m_nCtrl_ID == (ID_104_Active_ACK | MAIN_104_Flag))
			m_pStatus_Element = pElem;
	}

	return bResult;
}

cl_104_Element *cl_104_Client::FindElement(uint64_t nAddress, bool bAutoInsert, bool bParameter)
{
	auto elem_iter = m_Elements.find(nAddress);
	if (elem_iter == m_Elements.end())
	{
		if (!bAutoInsert)
			return nullptr;

		// If not found, create element
		cl_104_Element_UPtr uElem = std::make_unique<cl_104_Element> (this, nAddress, bParameter);
		auto insert_pair = m_Elements.insert(std::make_pair(nAddress, std::move(uElem)));
		elem_iter = insert_pair.first;
	}
	return (elem_iter->second).get();
}

cl_104_Element *cl_104_Client::FindElementName(wxString szName)
{
	return nullptr;
}

void cl_104_Client::ConnectionHandler(void* parameter, CS104_Connection connection, CS104_ConnectionEvent event)
{
	cl_104_Client *p104Client = ((cl_104_Client*)parameter);
	switch (event)
	{
		case CS104_CONNECTION_OPENED:
			{
				p104Client->m_bConnected = true;
				CS104_Connection_sendStartDT(connection);
				CS104_Connection_sendInterrogationCommand(connection, CS101_COT_ACTIVATION, 0xFFFF, IEC60870_QOI_STATION);

				wxString szIP = p104Client->m_Server_Addr[p104Client->m_nAct_Srv_Index].IPAddress();
				int nPort = p104Client->m_Server_Addr[p104Client->m_nAct_Srv_Index].Service();
				break;
			}
		case CS104_CONNECTION_CLOSED:
			if (p104Client->m_bConnected)
			{
				wxString szIP = p104Client->m_Server_Addr[p104Client->m_nAct_Srv_Index].IPAddress();
				int nPort = p104Client->m_Server_Addr[p104Client->m_nAct_Srv_Index].Service();
				p104Client->CloseConnection();
			}
			break;

		case CS104_CONNECTION_STARTDT_CON_RECEIVED:
			break;

		case CS104_CONNECTION_STOPDT_CON_RECEIVED:
			break;
	}
}

bool cl_104_Client::ASDU_ReceivedHandler (void* parameter, int address, CS101_ASDU asdu)
{
	cl_104_Client *p104Client = ((cl_104_Client*)parameter);

	IEC60870_5_TypeID n104_Type = CS101_ASDU_getTypeID(asdu);
	if (n104_Type == C_IC_NA_1)		//C_IC_NA_1
		return p104Client->Rx_Interrogation(asdu);

	uint64_t nCA_Part = ((uint64_t)CS101_ASDU_getCA(asdu)) << 24;
	uint8_t nCOT = CS101_ASDU_getCOT(asdu);

	cl_104_Data *pData;
	for (int i = 0; i < CS101_ASDU_getNumberOfElements(asdu); i++)
	{
		InformationObject IO = CS101_ASDU_getElement(asdu, i);
		int nIOA = InformationObject_getObjectAddress(IO);

		uint64_t nAddr = (nCA_Part | nIOA);
		cl_104_Element *pElem = p104Client->FindElement(nAddr);
		if (pElem == nullptr)
		{
			continue;
		}

		pData = cl_104_Data::Create_from_ASDU(asdu, i);
		if (pData == nullptr)
		{
			int nType = CS101_ASDU_getTypeID(asdu);
			continue;
		}

		pData->SetValue(IO, asdu);
		double fValue = pData->GetFltValue();
		pElem->NewData(pData);
		InformationObject_destroy(IO);

		if (pElem->m_bPropagate)
		{
			for (auto iter = p104Client->m_Clients.begin(); iter != p104Client->m_Clients.end(); ++iter)
			{
				cl_Client *pClient = *iter;
				uint64_t nID = pClient->Find_CmdElement_ID(pElem);
				if (nID != 0)
				{
					std::unique_ptr<cl_Poke_Command> uCmd = std::make_unique<cl_Poke_Command>(nID);
					uCmd->m_lstValue.push_back(std::make_unique<cl_Float_Poke_Value>(fValue, nCOT));
					pClient->m_Client_Rx.Send(uCmd.get());
				}
				else
					wxLogDebug("");
			}

			if (pElem->m_nACK_Address != 0)
			{
				if ((n104_Type == M_ME_NC_1) || (n104_Type == M_ME_TF_1) || (n104_Type == C_SE_NC_1))
				{
					p104Client->Send_SetPoint_ShrtFP(pElem, fValue, ((nCOT == CS101_COT_ACTIVATION) ? CS101_COT_ACTIVATION_CON : CS101_COT_SPONTANEOUS), false);
					if (nCOT == CS101_COT_ACTIVATION)
						p104Client->Send_SetPoint_ShrtFP(pElem, fValue, CS101_COT_ACTIVATION_TERMINATION, false);

					p104Client->Send_MeasuredValueShort(pElem, fValue, CS101_COT_SPONTANEOUS, true);
				}
				if ((n104_Type == M_SP_NA_1) || (n104_Type == M_SP_TB_1) || (n104_Type == C_SC_NA_1))
				{
					p104Client->Send_SingleCommand(pElem, fValue > 0.5, ((nCOT == CS101_COT_ACTIVATION) ? CS101_COT_ACTIVATION_CON : CS101_COT_SPONTANEOUS), false);
					if (nCOT == CS101_COT_ACTIVATION)
						p104Client->Send_SingleCommand(pElem, fValue > 0.5, CS101_COT_ACTIVATION_TERMINATION, false);

					p104Client->SinglePointInformation(pElem, fValue > 0.5, CS101_COT_SPONTANEOUS, true);
				}
				else if ((n104_Type == M_DP_NA_1) || (n104_Type == M_DP_TB_1) || (n104_Type == C_DC_NA_1))
				{
					p104Client->Send_DoubleCommand(pElem, std::round(fValue), ((nCOT == CS101_COT_ACTIVATION) ? CS101_COT_ACTIVATION_CON : CS101_COT_SPONTANEOUS), false);
					if (nCOT == CS101_COT_ACTIVATION)
						p104Client->Send_DoubleCommand(pElem, std::round(fValue), CS101_COT_ACTIVATION_TERMINATION, false);

					p104Client->DoublePointInformation(pElem, std::round(fValue), CS101_COT_SPONTANEOUS, true);
				}
				else
					wxLogDebug("");
			}
		}
	}
	return true;
}

bool cl_104_Client::Rx_Interrogation(CS101_ASDU asdu)
{
	InterrogationCommand IC = (InterrogationCommand)CS101_ASDU_getElement(asdu, 0);
	uint8_t QOI = InterrogationCommand_getQOI(IC);
	InformationObject_destroy((InformationObject)IC);


	if ((CS101_ASDU_getCOT(asdu) == CS101_COT_ACTIVATION) && (QOI == IEC60870_QOI_STATION))
	{
		CS101_AppLayerParameters alParams = CS104_Connection_getAppLayerParameters(m_104_Connection);

		CS101_ASDU newasdu = CS101_ASDU_create(alParams, false, CS101_COT_ACTIVATION_CON, 0, 0xFFFF, false, false);
		InformationObject io = (InformationObject)InterrogationCommand_create(nullptr, 0xFFFF, IEC60870_QOI_STATION);
		CS101_ASDU_addInformationObject(newasdu, io);
		InformationObject_destroy(io);
		CS104_Connection_sendASDU(m_104_Connection, newasdu);
		CS101_ASDU_destroy(newasdu);

		uint32_t nCA = 0, nIOA;
		for (auto iter = m_Interrog_Elements.begin(); iter != m_Interrog_Elements.end(); ++iter)
		{
			cl_104_Element *pElem = *iter;
			if (pElem->m_nType == 0)
				continue;

			if (pElem->m_nACK_Address != 0)
			{
				nCA = cl_104_Element::GetCAPart(pElem->m_nACK_Address);
				nIOA = cl_104_Element::GetIOAPart(pElem->m_nACK_Address);
			}
			else
			{
				nCA = pElem->GetCA();
				nIOA = pElem->GetIOA();
			}

			CS101_ASDU newAsdu = CS101_ASDU_create(alParams, false, CS101_COT_INTERROGATED_BY_STATION, 0, nCA, false, false);
			if (pElem->m_nType == M_SP_NA_1)
			{
				io = (InformationObject)SinglePointInformation_create(nullptr, nIOA, (pElem->m_fValue > 0.5) ? true : false, IEC60870_QUALITY_GOOD);
			}
			else if (pElem->m_nType == M_DP_NA_1)
			{
				int nValue = ((int)(pElem->m_fValue + 0.1)) % 3;
				io = (InformationObject)DoublePointInformation_create(nullptr, nIOA, (DoublePointValue)nValue, IEC60870_QUALITY_GOOD);
			}
			else if (pElem->m_nType == M_ME_NC_1)
			{
				io = (InformationObject)MeasuredValueShort_create(nullptr, nIOA, pElem->m_fValue, IEC60870_QUALITY_GOOD);
			}
			else
				continue;
			CS101_ASDU_addInformationObject(newAsdu, io);
			InformationObject_destroy(io);
			CS104_Connection_sendASDU(m_104_Connection, newAsdu);
			CS101_ASDU_destroy(newAsdu);
		}

		newasdu = CS101_ASDU_create(alParams, false, CS101_COT_ACTIVATION_TERMINATION, 0, 0xFFFF, false, false);
		io = (InformationObject)InterrogationCommand_create(nullptr, 0xFFFF, IEC60870_QOI_STATION);
		CS101_ASDU_addInformationObject(newasdu, io);
		InformationObject_destroy(io);
		CS104_Connection_sendASDU(m_104_Connection, newasdu);
		CS101_ASDU_destroy(newasdu);

		return true;
	}
	return false;
}

void cl_104_Client::AddInterrog(cl_104_Element *pElem)
{
	for (auto iter = m_Interrog_Elements.begin(); iter != m_Interrog_Elements.end(); ++iter)
		if (*iter == pElem)
		{return;
		}

	m_Interrog_Elements.push_back(pElem);
}

void cl_104_Client::ClientConnected(cl_Client *pClient)
{
	m_Clients.push_back(pClient);
}

void cl_104_Client::ClientClosed(cl_Client *pClient)
{
	for (auto iter = m_Clients.begin(); iter != m_Clients.end(); ++iter)
	{
		if ((*iter) == pClient)
		{);
			m_Clients.erase(iter);
			return;
		}
	}
}

bool cl_104_Client::OpenReplay(wxString szPath)
{
	if (szPath.Last() != '/')
		szPath += '/';
	szPath += m_szName + '/';
	wxDir dir(szPath);
	if (!dir.IsOpened())
		 return false;

	wxString szFileName;
	bool bCont = dir.GetFirst(&szFileName, "*.txt", wxDIR_FILES);
	while (bCont)
	{
		uint64_t nAddr;
		wxString szAddr = szFileName.BeforeLast('.');
		if (!cl_104_Element::StrToAddr(szAddr, nAddr, false))
		{
			bCont = dir.GetNext(&szFileName);
			continue;
		}

		cl_104_Element_UPtr uElem = std::make_unique<cl_104_Element> (this, nAddr, false);
		if (!uElem->OpenReplay(szPath + szFileName))
		{
			bCont = dir.GetNext(&szFileName);
			continue;
		}
		m_Elements.insert(std::make_pair(nAddr, std::move(uElem)));
		bCont = dir.GetNext(&szFileName);
	}

	return true;
}

void cl_104_Client::Send_MeasuredValueShort(cl_104_Element *pElem, double fValue, uint8_t nCOT, bool bAckAddr)
{
	uint64_t nAddr = bAckAddr ? pElem->m_nACK_Address : pElem->m_nAddress;
	if (!m_bConnected)
	{
		return;
	}
	InformationObject io = (InformationObject)MeasuredValueShort_create(nullptr, (nAddr & 0x00FFFFFF), fValue, IEC60870_QUALITY_GOOD);
	CS104_Connection_sendProcessCommandEx(m_104_Connection, (CS101_CauseOfTransmission)nCOT, ((nAddr >> 24) & 0x0000FFFF), io);
	InformationObject_destroy(io);
}

void cl_104_Client::Send_SetPoint_ShrtFP(cl_104_Element *pElem, double fValue, uint8_t nCOT, bool bAckAddr)
{
	uint64_t nAddr = bAckAddr ? pElem->m_nACK_Address : pElem->m_nAddress;
	if (!m_bConnected)
	{
		return;
	}
	InformationObject io = (InformationObject)SetpointCommandShort_create(nullptr, (nAddr & 0x00FFFFFF), fValue, false, 0);
	CS104_Connection_sendProcessCommandEx(m_104_Connection, (CS101_CauseOfTransmission)nCOT, ((nAddr >> 24) & 0x0000FFFF), io);
	InformationObject_destroy(io);
}

void cl_104_Client::SinglePointInformation(cl_104_Element *pElem, bool bValue, uint8_t nCOT, bool bAckAddr)
{
	uint64_t nAddr = bAckAddr ? pElem->m_nACK_Address : pElem->m_nAddress;
	if (!m_bConnected)
	{
		return;
	}
	InformationObject io = (InformationObject)SinglePointInformation_create(nullptr, (nAddr & 0x00FFFFFF), bValue, IEC60870_QUALITY_GOOD);
	CS104_Connection_sendProcessCommandEx(m_104_Connection, (CS101_CauseOfTransmission)nCOT, ((nAddr >> 24) & 0x0000FFFF), io);
	InformationObject_destroy(io);
}

void cl_104_Client::Send_SingleCommand(cl_104_Element *pElem, bool bValue, uint8_t nCOT, bool bAckAddr)
{
	uint64_t nAddr = bAckAddr ? pElem->m_nACK_Address : pElem->m_nAddress;
	if (!m_bConnected)
	{
		return;
	}
	InformationObject io = (InformationObject)SingleCommand_create(nullptr, (nAddr & 0x00FFFFFF), bValue, false, 0);
	CS104_Connection_sendProcessCommandEx(m_104_Connection, (CS101_CauseOfTransmission)nCOT, ((nAddr >> 24) & 0x0000FFFF), io);
	InformationObject_destroy(io);
}

void cl_104_Client::DoublePointInformation(cl_104_Element *pElem, int nValue, uint8_t nCOT, bool bAckAddr)
{
	uint64_t nAddr = bAckAddr ? pElem->m_nACK_Address : pElem->m_nAddress;
	if (!m_bConnected)
	{
		return;
	}
	InformationObject io = (InformationObject)DoublePointInformation_create(nullptr, (nAddr & 0x00FFFFFF), (DoublePointValue)(nValue & 0x03), IEC60870_QUALITY_GOOD);
	CS104_Connection_sendProcessCommandEx(m_104_Connection, (CS101_CauseOfTransmission)nCOT, ((nAddr >> 24) & 0x0000FFFF), io);
	InformationObject_destroy(io);
}

void cl_104_Client::Send_DoubleCommand(cl_104_Element *pElem, int nValue, uint8_t nCOT, bool bAckAddr)
{
	uint64_t nAddr = bAckAddr ? pElem->m_nACK_Address : pElem->m_nAddress;
	if (!m_bConnected)
	{
		return;
	}
	InformationObject io = (InformationObject)DoubleCommand_create(nullptr, (nAddr & 0x00FFFFFF), nValue & 0x03, false, 0);
	CS104_Connection_sendProcessCommandEx(m_104_Connection, (CS101_CauseOfTransmission)nCOT, ((nAddr >> 24) & 0x0000FFFF), io);
	InformationObject_destroy(io);
}


// cl_104_Data
cl_104_Data::cl_104_Data(CS101_ASDU ASDU, int nIx)
	: m_bTimeSet(false), m_nListPosition(-1)
{
	m_nType = CS101_ASDU_getTypeID(ASDU);
	m_nCA = CS101_ASDU_getCA(ASDU);
	InformationObject IO = (InformationObject)CS101_ASDU_getElement(ASDU, nIx);
	m_nIOA = InformationObject_getObjectAddress(IO);
	InformationObject_destroy(IO);
}

cl_104_Data::~cl_104_Data()
{
}

void cl_104_Data::SetCOT(CS101_ASDU ASDU)
{
	m_nCOT = CS101_ASDU_getCOT(ASDU);
}

wxString cl_104_Data::GetStrType()
{
	return TypeID_toString((TypeID)m_nType);
}

wxString cl_104_Data::GetStrTime()
{
	if (!m_bTimeSet || !m_dtTime.IsValid())
		return wxEmptyString;

	return m_dtTime.Format("%Y-%m-%d %H:%M:%S.%l");
}

wxDateTime cl_104_Data::GetTime()
{
	if (m_bTimeSet)
		return m_dtTime;

	return wxDateTime::UNow();
}

void cl_104_Data::SetTime(InformationObject IO)
{
	CP56Time2a xTime = MeasuredValueShortWithCP56Time2a_getTimestamp((MeasuredValueShortWithCP56Time2a)IO);
	uint64_t nTime = CP56Time2a_toMsTimestamp(xTime);
	m_dtTime.Set((time_t)(nTime / 1000));
	m_dtTime.SetMillisecond(nTime % 1000);
	m_bTimeSet = true;
}

cl_104_Data* cl_104_Data::Create_from_ASDU(CS101_ASDU ASDU, int nIx)
{
	cl_104_Data *pData = nullptr;

	IEC60870_5_TypeID Type = CS101_ASDU_getTypeID(ASDU);
	if (Type == M_ME_NB_1)												// 11
		pData = new cl_104_Meas_Int(ASDU, nIx);
	else if ((Type == M_SP_NA_1) || (Type == M_SP_TB_1))		// 1 30
		pData = new cl_104_Single_Info(ASDU, nIx);
	else if ((Type == M_DP_NA_1) || (Type == M_DP_TB_1))		// 3 31
		pData = new cl_104_Double_Info(ASDU, nIx);
	else if ((Type == M_ME_NC_1) || (Type == M_ME_TF_1))		// 13 36
		pData = new cl_104_Meas_Float(ASDU, nIx);
	else if (Type == M_ST_TB_1)										// 32
		pData = new cl_104_Step_Pos_Info(ASDU, nIx);
	else if (Type == C_SE_NC_1)										// 50
		pData = new cl_104_Meas_Float(ASDU, nIx);
	else if (Type == C_SC_NA_1)										// 45
		pData = new cl_104_Single_Info(ASDU, nIx);
	else if (Type == C_DC_NA_1)										// 46
		pData = new cl_104_Double_Info(ASDU, nIx);

	if (pData == nullptr)
		wxLogDebug("");

	return pData;
}

int cl_104_Data::GetValType()
{
	return (m_nIOA & 0x0000FF00) >> 8;
}


// cl_104_Meas_Int
cl_104_Meas_Int::cl_104_Meas_Int(CS101_ASDU ASDU, int nIx)
	: cl_104_Data(ASDU, nIx)
{
}

wxString cl_104_Meas_Int::GetStrValue()
{
	return wxString::Format("%i", m_nValue);
}

void cl_104_Meas_Int::SetValue(InformationObject IO, CS101_ASDU ASDU)
{
	SetCOT(ASDU);
	m_nValue = MeasuredValueScaled_getValue((MeasuredValueScaled)IO);
	m_nQualDesc = MeasuredValueScaled_getQuality((MeasuredValueScaled)IO);
}


// cl_104_Single_Info
cl_104_Single_Info::cl_104_Single_Info(CS101_ASDU ASDU, int nIx)
	: cl_104_Data(ASDU, nIx)
{
}

wxString cl_104_Single_Info::GetStrValue()
{
	return wxString(m_bValue ? wxT("true") : wxT("false"));
}

void cl_104_Single_Info::SetValue(InformationObject IO, CS101_ASDU ASDU)
{
	SetCOT(ASDU);
	m_bValue = SinglePointInformation_getValue((SinglePointInformation)IO);
	m_nQualDesc = SinglePointInformation_getQuality((SinglePointInformation)IO);

	if (m_nType == C_SC_NA_1)
		wxLogDebug("");
}


// cl_104_Double_Info
cl_104_Double_Info::cl_104_Double_Info(CS101_ASDU ASDU, int nIx)
	: cl_104_Data(ASDU, nIx)
{
}

wxString cl_104_Double_Info::GetStrValue()
{
	return wxString::Format("%i", m_nValue);
}

void cl_104_Double_Info::SetValue(InformationObject IO, CS101_ASDU ASDU)
{
	SetCOT(ASDU);
	m_nValue = DoublePointInformation_getValue((DoublePointInformation)IO) &0x03;
	m_nQualDesc = DoublePointInformation_getQuality((DoublePointInformation)IO);

	IEC60870_5_TypeID Type = CS101_ASDU_getTypeID(ASDU);
	if (Type == M_DP_TB_1)
		SetTime(IO);
	else
		m_bTimeSet = false;

	if (m_nType == C_SC_NA_1)
		wxLogDebug("");
}

// cl_104_Meas_Float
cl_104_Meas_Float::cl_104_Meas_Float(CS101_ASDU ASDU, int nIx)
	: cl_104_Data(ASDU, nIx)
{}

wxString cl_104_Meas_Float::GetStrValue()
{return wxString::Format("%0.5f", m_fValue);}

void cl_104_Meas_Float::SetValue(InformationObject IO, CS101_ASDU ASDU)
{
	SetCOT(ASDU);
	m_fValue = MeasuredValueShort_getValue((MeasuredValueShort)IO);
	m_nQualDesc = MeasuredValueShort_getQuality((MeasuredValueShort)IO);

	IEC60870_5_TypeID Type = CS101_ASDU_getTypeID(ASDU);
	if (Type == M_ME_TF_1)
		SetTime(IO);
	else
		m_bTimeSet = false;

	if (m_nType == C_SE_NC_1)
		wxLogDebug("");
}

// cl_104_SetPoint_Float
cl_104_SetPoint_Float::cl_104_SetPoint_Float(CS101_ASDU ASDU, int nIx)
	: cl_104_Data(ASDU, nIx)
{}

wxString cl_104_SetPoint_Float::GetStrValue()
{return wxString::Format("%0.5f", m_fValue);}

void cl_104_SetPoint_Float::SetValue(InformationObject IO, CS101_ASDU ASDU)
{
	SetCOT(ASDU);
	m_fValue = SetpointCommandShort_getValue((SetpointCommandShort)IO);
}


// cl_104_Step_Pos_Info
cl_104_Step_Pos_Info::cl_104_Step_Pos_Info(CS101_ASDU ASDU, int nIx)
	: cl_104_Data(ASDU, nIx)
{}

wxString cl_104_Step_Pos_Info::GetStrValue()
{return wxString::Format("%i", m_nValue);}

void cl_104_Step_Pos_Info::SetValue(InformationObject IO, CS101_ASDU ASDU)
{
	SetCOT(ASDU);
	m_nValue = StepPositionInformation_getValue((StepPositionInformation)IO);
	m_nQualDesc = StepPositionInformation_getQuality((StepPositionInformation)IO);
}

// cl_ConnectServer
cl_ConnectServer::cl_ConnectServer(cl_MainApp *pParent)
	: m_pParent(pParent), m_bStop(false), m_Semaphore(0, 0)
{}

void cl_ConnectServer::Run()
{
	std::chrono::seconds sec(2);
	while (!m_bStop)
	{
		bool bSleep = true;

		// Take care about dying connections - disconnect them
		for (auto iter = m_pParent->m_104_Clients.begin(); iter != m_pParent->m_104_Clients.end(); ++iter)
		{
			cl_104_Client *pClient = (*iter).get();
			if (pClient->m_bDisConnect)
			{
				if (pClient->m_104_Connection != nullptr)
				{
					CS104_Connection_close(pClient->m_104_Connection);
					CS104_Connection_destroy(pClient->m_104_Connection);
				}
				pClient->m_bConnected = false;
				pClient->m_104_Connection = nullptr;
				pClient->m_bDisConnect = false;
			}
		}

		// Try to keep every client connected
		for (auto iter = m_pParent->m_104_Clients.begin(); iter != m_pParent->m_104_Clients.end(); ++iter)
		{
			cl_104_Client *pClient = (*iter).get();
			if (!pClient->m_bConnected)
			{
				bSleep = false;
				if (!pClient->Connect())
					pClient->SetNextServer();
			}
		}

		if (bSleep)
		{
			m_Semaphore.Wait();
		}
	}
}

// cl_104_Element
cl_104_Element::cl_104_Element(cl_104_Client *pClient, uint64_t nAddress, bool bIsParameter)
	: m_pClient(pClient), m_nAddress(nAddress), m_nACK_Address(0), m_bPropagate(false), m_bSetPoint(false), m_nCtrl_ID(0), m_nType(0)
{
	m_fValue = 0.;
}

cl_104_Element::~cl_104_Element()
{
	if (m_FileTxt.IsOpened())
	{
		m_FileTxt.Flush();
		m_FileTxt.Close();
	}
}

uint32_t cl_104_Element::GetIOA()
{
	return m_nAddress & 0x00FFFFFF;
}

uint32_t cl_104_Element::GetCA()
{
	return (m_nAddress >> 24) & 0x0000FFFF;
}

uint32_t cl_104_Element::GetIOAPart(uint64_t nAddress) { return nAddress & 0x00FFFFFF; }
uint32_t cl_104_Element::GetCAPart(uint64_t nAddress) { return (nAddress >> 24) & 0x0000FFFF; }


void cl_104_Element::LogValue(cl_104_Data *pData)
{
	if (!m_FileTxt.IsOpened())
		return;

	wxString szData = wxDateTime::UNow().Format("%Y-%m-%d %H:%M:%S.%l");
	szData += wxString::Format("\t%i\t%s\t%i\t" + pData->GetStrQual(), pData->GetValType(), pData->GetStrValue(), pData->m_nCOT);
	if (pData->m_bTimeSet)
		szData += "\t" + pData->GetStrTime();
	m_FileTxt.Write(szData + "\n");
}

bool cl_104_Element::StrToAddr(wxString szStr, uint64_t &nValue, bool bHex)
{
	uint64_t nVal = 0;
	wxStringTokenizer AddrTok(szStr, wxT("."), wxTOKEN_STRTOK);
	if (AddrTok.CountTokens() != 5)
		return false;

	long nTmp;
	for (int i = 4; i >= 0; i--)
	{
		if (!AddrTok.GetNextToken().ToLong(&nTmp, (bHex ? 16 : 10)))
			return false;

		nVal += (uint64_t)nTmp << (8 * i);
	}

	nValue = nVal;
	return true;
}

wxString cl_104_Element::AddrToStr(uint64_t nAddress)
{
	return wxString::Format("%03i.%03i.%03i.%03i.%03i",
									(uint8_t)(nAddress >> 32) & 0x000000FF,
									(uint8_t)(nAddress >> 24) & 0x000000FF,
									(uint8_t)(nAddress >> 16) & 0x000000FF,
									(uint8_t)(nAddress >> 8) & 0x000000FF,
									(uint8_t)(nAddress & 0x000000FF)
									);
}

int cl_104_Element::AddrToValType(uint64_t nAddr)
{
	return nAddr;
}

void cl_104_Element::NewData(cl_104_Data *pData)
{
	m_dtLastData = pData->GetTime();
	m_fValue = pData->GetFltValue();
	m_nQuality = pData->m_nQualDesc;
	delete pData;
}

bool cl_104_Element::HaveNewData(wxDateTime dtNewerThan)
{
	if (m_dtLastData.IsValid())
	{
		return m_dtLastData >= dtNewerThan;
	}
	return false;
}

cl_Elem_104_Value * cl_104_Element::GetData()
{
	cl_Elem_104_Value *pValue = new cl_Elem_104_Value;

	pValue->m_nAddress = m_nAddress;
	pValue->m_dtLastData = m_dtLastData;
	pValue->m_fValue = m_fValue;
	pValue->m_nQuality = m_nQuality;
	return pValue;
}

void cl_104_Element::SetLogData(cl_Replay_Data *pLine)
{
	m_dtLastData = pLine->m_dtTime;
	m_fValue = pLine->m_fValue;
	m_nQuality = pLine->m_nQuality;
}







