/***************************************************************
 * Name:      Commands.cpp
 * Purpose:   Code for DNCors interface commands
 **************************************************************/

#include "wx_pch.h"
#include "Commands.h"

//----------------------------------------------------------------------------
// cl_Command
//----------------------------------------------------------------------------
cl_Command::cl_Command()
{
}

void cl_Command::Serialize (cl_Serializer *pSerializer)
{
	pSerializer->AddTLV_U32(TAG_u32_Client_ID, m_nSessionID);
}

bool cl_Command::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_u32_Client_ID: cl_Serializer::GetTLV_U32(nLength, pValue, m_nSessionID); break;
		default: bReturn = false; break;
	}
	return bReturn;
}

//----------------------------------------------------------------------------
// cl_Init_Cmd
//----------------------------------------------------------------------------
cl_Init_Cmd::cl_Init_Cmd()
{;}

cl_Init_Cmd::cl_Init_Cmd(wxString m_sz104_Server)
	: m_sz104_Server(m_sz104_Server)
{;}

void cl_Init_Cmd::Serialize(cl_Serializer *pSerializer)
{
	cl_Command::Serialize(pSerializer);
	pSerializer->AddTLV_UTF8(TAG_sz8_Server, m_sz104_Server);
}

bool cl_Init_Cmd::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_sz8_Server: cl_Serializer::GetTLV_UTF8(nLength, pValue, m_sz104_Server); break;
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Command::Deserialize(nTag, nLength, pValue);

	return true;
}

//----------------------------------------------------------------------------
// cl_Init_Answer
//----------------------------------------------------------------------------
cl_Init_Answer::cl_Init_Answer()
{}

cl_Init_Answer::cl_Init_Answer(bool bInit_OK, uint8_t nMode)
	: m_bInit_OK(bInit_OK), m_nMode(nMode)
{}

void cl_Init_Answer::Serialize(cl_Serializer *pSerializer)
{
	cl_Cmd_Answer::Serialize(pSerializer);

	pSerializer->AddTLV_Bool(TAG_b_OK, m_bInit_OK);
	pSerializer->AddTLV_U8(TAG_u8_Mode, m_nMode);
}

bool cl_Init_Answer::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_b_OK: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bInit_OK); break;
		case TAG_u8_Mode: cl_Serializer::GetTLV_U8(nLength, pValue, m_nMode); break;
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Cmd_Answer::Deserialize(nTag, nLength, pValue);

	return true;
}

//----------------------------------------------------------------------------
// cl_Elem_Stub - only for (de)serialization
//----------------------------------------------------------------------------
cl_Elem_Stub::cl_Elem_Stub()
	: m_nID(0), m_n104Addr(0), m_n104_ACK_Adress(0), m_n104Type(0), m_bSetPoint(false)
{
}

cl_Elem_Stub::cl_Elem_Stub(uint32_t nID, uint64_t n104Addr, bool bSetPoint, bool bPropagate)
	: m_nID(nID), m_n104Addr(n104Addr), m_n104_ACK_Adress(0), m_n104Type(0), m_bSetPoint(bSetPoint), m_bPropagate(bPropagate)
{
}

void cl_Elem_Stub::Serialize(cl_Serializer *pSerializer)
{
	pSerializer->AddTLV_U32(TAG_u32_ID, m_nID);
	pSerializer->AddTLV_U64(TAG_u64_104ADDR, m_n104Addr);
	pSerializer->AddTLV_U64(TAG_u64_ACK_104ADDR, m_n104_ACK_Adress);
	pSerializer->AddTLV_Bool(TAG_b_SetPoint, m_bSetPoint);
	pSerializer->AddTLV_Bool(TAG_b_Propagate, m_bPropagate);
	pSerializer->AddTLV_U8(TAG_u8_104TYPE, m_n104Type);
}

bool cl_Elem_Stub::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	switch(nTag)
	{
		case TAG_b_Propagate: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bPropagate); break;
		case TAG_b_SetPoint: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bSetPoint); break;
		case TAG_u64_104ADDR: cl_Serializer::GetTLV_U64(nLength, pValue, m_n104Addr); break;
		case TAG_u64_ACK_104ADDR: cl_Serializer::GetTLV_U64(nLength, pValue, m_n104_ACK_Adress); break;
		case TAG_u8_104TYPE: cl_Serializer::GetTLV_U8(nLength, pValue, m_n104Type); break;
		case TAG_u32_ID: cl_Serializer::GetTLV_U32(nLength, pValue, m_nID); break;
	}
	return true;
}

wxString cl_Elem_Stub::AddrToStr(uint64_t nAddress)
{
	return wxString::Format("%03i.%03i.%03i.%03i.%03i",
									(uint8_t)(nAddress >> 32) & 0x000000FF,
									(uint8_t)(nAddress >> 24) & 0x000000FF,
									(uint8_t)(nAddress >> 16) & 0x000000FF,
									(uint8_t)(nAddress >> 8) & 0x000000FF,
									(uint8_t)(nAddress & 0x000000FF)
									);
}

//----------------------------------------------------------------------------
// cl_Reg_Elems_Cmd
//----------------------------------------------------------------------------
cl_Reg_Elems_Cmd::cl_Reg_Elems_Cmd()
{
}

cl_Reg_Elems_Cmd::~cl_Reg_Elems_Cmd()
{
	while (!m_Elems.empty())
	{
		delete m_Elems.front();
		m_Elems.pop_front();
	}
}

void cl_Reg_Elems_Cmd::Serialize (cl_Serializer *pSerializer)
{
	cl_Command::Serialize(pSerializer);

	for (cl_SerializableObject *pElem : m_Elems)
		pSerializer->Serialize(pElem);
}

bool cl_Reg_Elems_Cmd::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Command::Deserialize(nTag, nLength, pValue);
	return true;
}

bool cl_Reg_Elems_Cmd::ProcessNewSubObject(cl_SerializableObject *pObj)
{
	m_Elems.push_back(static_cast <cl_Elem_Stub*> (pObj));
	return true;
}

//----------------------------------------------------------------------------
// cl_Reg_Elems_Answer
//----------------------------------------------------------------------------
cl_Reg_Elems_Answer::cl_Reg_Elems_Answer()
	: m_bOK(false)
{}

cl_Reg_Elems_Answer::cl_Reg_Elems_Answer(bool bOK)
	: m_bOK(bOK)
{}

void cl_Reg_Elems_Answer::Serialize(cl_Serializer *pSerializer)
{
	cl_Cmd_Answer::Serialize(pSerializer);
	pSerializer->AddTLV_Bool(TAG_b_OK, m_bOK);
}

bool cl_Reg_Elems_Answer::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_b_OK: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bOK); break;
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Cmd_Answer::Deserialize(nTag, nLength, pValue);

	return true;
}

//----------------------------------------------------------------------------
// cl_Get_Data_Cmd - get current data
//----------------------------------------------------------------------------
cl_Get_Data_Cmd::cl_Get_Data_Cmd()
{
}

cl_Get_Data_Cmd::cl_Get_Data_Cmd(wxDateTime NewerThan, bool bStart)
	: m_NewerThan(NewerThan), m_bStart(bStart)
{
}

void cl_Get_Data_Cmd::Serialize(cl_Serializer *pSerializer)
{
	cl_Command::Serialize(pSerializer);

	pSerializer->AddTLV_Date(TAG_dt_DateTime, m_NewerThan);
	pSerializer->AddTLV_Bool(TAG_b_Value, m_bStart);
}

bool cl_Get_Data_Cmd::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_dt_DateTime: cl_Serializer::GetTLV_Date(nLength, pValue, m_NewerThan); break;
		case TAG_b_Value: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bStart); break;
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Command::Deserialize(nTag, nLength, pValue);

	return bReturn;
}

//----------------------------------------------------------------------------
// cl_Data_Answer
//----------------------------------------------------------------------------
cl_Data_Answer::cl_Data_Answer()
	: m_bFinal(false)
{
}

void cl_Data_Answer::Serialize(cl_Serializer *pSerializer)
{
	cl_Cmd_Answer::Serialize(pSerializer);
	pSerializer->AddTLV_Bool(TAG_b_Value, m_bFinal);

	for(auto iter = m_Value.begin(); iter != m_Value.end(); ++iter)
		pSerializer->Serialize(iter->get());
}

bool cl_Data_Answer::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_b_Value: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bFinal); break;
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Cmd_Answer::Deserialize(nTag, nLength, pValue);

	return bReturn;
}

bool cl_Data_Answer::ProcessNewSubObject(cl_SerializableObject *pObj)
{
	m_Value.push_back(std::unique_ptr<cl_Elem_104_Value>((cl_Elem_104_Value*)(pObj)));
	return true;
}

//----------------------------------------------------------------------------
// cl_Elem_104_Value
//----------------------------------------------------------------------------
cl_Elem_104_Value::cl_Elem_104_Value()
	: m_nAddress(0), m_fValue(0.), m_nQuality(0)
{
}

void cl_Elem_104_Value::Serialize(cl_Serializer *pSerializer)
{
	pSerializer->AddTLV_U64(TAG_u64_104ADDR, m_nAddress);
	pSerializer->AddTLV_Dbl(TAG_dbl_Value, m_fValue);
	pSerializer->AddTLV_U32(TAG_u32_Quality, m_nQuality);
	pSerializer->AddTLV_Date(TAG_dt_DateTime, m_dtLastData);
}

bool cl_Elem_104_Value::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_u64_104ADDR: cl_Serializer::GetTLV_U64(nLength, pValue, m_nAddress); break;
		case TAG_dbl_Value: cl_Serializer::GetTLV_Dbl(nLength, pValue, m_fValue); break;
		case TAG_u32_Quality: cl_Serializer::GetTLV_U32(nLength, pValue, m_nQuality); break;
		case TAG_dt_DateTime: cl_Serializer::GetTLV_Date(nLength, pValue, m_dtLastData); break;
		default: bReturn = false;
	}

	return bReturn;
}

//----------------------------------------------------------------------------
// cl_Poke_Value
//----------------------------------------------------------------------------
//void cl_Poke_Value::Serialize(cl_Serializer *pSerializer)
//{
//	pSerializer->AddTLV_U32(TAG_u32_ID, m_nID);
//	pSerializer->AddTLV_Date(TAG_dt_DateTime, m_dtLastData);
//}
//
//bool cl_Poke_Value::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
//{
//	bool bReturn = true;
//	switch(nTag)
//	{
//		case TAG_u32_ID: cl_Serializer::GetTLV_U32(nLength, pValue, m_nID); break;
//		case TAG_dt_DateTime: cl_Serializer::GetTLV_Date(nLength, pValue, m_dtLastData); break;
//		default: bReturn = false;
//	}
//
////	if (!bReturn)
////		bReturn = cl_104_Element::Deserialize(nTag, nLength, pValue);
//
//	return bReturn;
//}

//----------------------------------------------------------------------------
// cl_Bool_Poke_Value
//----------------------------------------------------------------------------
cl_Bool_Poke_Value::cl_Bool_Poke_Value()
{
	m_bValue = 0;
	m_nCOT = CS101_COT_SPONTANEOUS;
}

cl_Bool_Poke_Value::cl_Bool_Poke_Value(bool bValue, uint8_t nCOT)
	: m_bValue(bValue), m_nCOT(nCOT)
{
}

void cl_Bool_Poke_Value::Serialize(cl_Serializer *pSerializer)
{
//	cl_Poke_Value::Serialize(pSerializer);

	pSerializer->AddTLV_Bool(TAG_b_Value, m_bValue);
	pSerializer->AddTLV_U8(TAG_u8_COT, m_nCOT);
}

bool cl_Bool_Poke_Value::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_b_Value: cl_Serializer::GetTLV_Bool(nLength, pValue, m_bValue); break;
		case TAG_u8_COT: cl_Serializer::GetTLV_U8(nLength, pValue, m_nCOT); break;
		default: bReturn = false;
	}

	return bReturn;
}

#if defined __WXDEBUG__
	void cl_Bool_Poke_Value::Dump(wxString szPrefix)
	{
		wxLogDebug(szPrefix + "value %s, ", (m_bValue ? "true" : "false"));
	}
#endif // defined

//----------------------------------------------------------------------------
// cl_4State_Poke_Value
//----------------------------------------------------------------------------
cl_4State_Poke_Value::cl_4State_Poke_Value()
{
	m_nValue = 0;
	m_nCOT = CS101_COT_SPONTANEOUS;
}

cl_4State_Poke_Value::cl_4State_Poke_Value(int nValue, uint8_t nCOT)
	: m_nValue(nValue & 0x03), m_nCOT(nCOT)
{
}

void cl_4State_Poke_Value::Serialize(cl_Serializer *pSerializer)
{
//	cl_Poke_Value::Serialize(pSerializer);

	pSerializer->AddTLV_U8(TAG_u8_Value, m_nValue);
	pSerializer->AddTLV_U8(TAG_u8_COT, m_nCOT);
}

bool cl_4State_Poke_Value::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_u8_Value: cl_Serializer::GetTLV_U8(nLength, pValue, m_nValue); break;
		case TAG_u8_COT: cl_Serializer::GetTLV_U8(nLength, pValue, m_nCOT); break;
		default: bReturn = false;
	}

	return bReturn;
}

#if defined __WXDEBUG__
	void cl_4State_Poke_Value::Dump(wxString szPrefix)
	{
		wxLogDebug(szPrefix + "value 0x%02X, ", m_nValue);
	}
#endif // defined

//----------------------------------------------------------------------------
// cl_Float_Poke_Value
//----------------------------------------------------------------------------
cl_Float_Poke_Value::cl_Float_Poke_Value()
{
	m_fValue = 0;
	m_nCOT = CS101_COT_SPONTANEOUS;
}

cl_Float_Poke_Value::cl_Float_Poke_Value(double fValue, uint8_t nCOT)
	: m_fValue(fValue), m_nCOT(nCOT)
{
}

void cl_Float_Poke_Value::Serialize(cl_Serializer *pSerializer)
{
//	cl_Poke_Value::Serialize(pSerializer);

	pSerializer->AddTLV_Dbl(TAG_dbl_Value, m_fValue);
	pSerializer->AddTLV_U8(TAG_u8_COT, m_nCOT);
}

bool cl_Float_Poke_Value::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_dbl_Value: cl_Serializer::GetTLV_Dbl(nLength, pValue, m_fValue); break;
		case TAG_u8_COT: cl_Serializer::GetTLV_U8(nLength, pValue, m_nCOT); break;
		default: bReturn = false;
	}

//	if (!bReturn)
//		bReturn = cl_Poke_Value::Deserialize(nTag, nLength, pValue);

	return bReturn;
}

#if defined __WXDEBUG__
	void cl_Float_Poke_Value::Dump(wxString szPrefix)
	{
		wxLogDebug(szPrefix + "value %f, ", m_fValue);
	}
#endif // defined

//----------------------------------------------------------------------------
// cl_Poke_Command
//----------------------------------------------------------------------------
cl_Poke_Command::cl_Poke_Command(uint32_t nElementID)
	: m_nElementID(nElementID)
{
}

void cl_Poke_Command::Serialize(cl_Serializer *pSerializer)
{
	cl_Command::Serialize(pSerializer);

	pSerializer->AddTLV_U32(TAG_u32_Value, m_nElementID);
	for(auto iter = m_lstValue.begin(); iter != m_lstValue.end(); ++iter)
		pSerializer->Serialize(iter->get());
}

bool cl_Poke_Command::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
	bool bReturn = true;
	switch(nTag)
	{
		case TAG_u32_Value: cl_Serializer::GetTLV_U32(nLength, pValue, m_nElementID); break;
		default: bReturn = false;
	}

	if (!bReturn)
		bReturn = cl_Command::Deserialize(nTag, nLength, pValue);

	return bReturn;
}

bool cl_Poke_Command::ProcessNewSubObject(cl_SerializableObject *pObj)
{
	m_lstValue.push_back(std::unique_ptr<cl_Poke_Value>((cl_Poke_Value*)(pObj)));
	return true;
}

#if defined __WXDEBUG__
	void cl_Poke_Command::Dump(wxString szPrefix)
	{
		wxLogDebug(szPrefix + "Poke command: elemID 0x%08X", m_nElementID);
		for(auto iter = m_lstValue.begin(); iter != m_lstValue.end(); ++iter)
			(*iter)->Dump(szPrefix);
	}
#endif // defined

//----------------------------------------------------------------------------
// CreateObjectByTag function - class factory like
//----------------------------------------------------------------------------
cl_SerializableObject *IEC104_CreateObjectByTag(uint32_t nTag)
{
	switch (nTag)
	{
		case TAG_CLASS_COMMAND: return new cl_Command;
		case TAG_CLASS_ANSW_ACK: return new cl_Ack_Answ;
		case TAG_CLASS_CMD_INIT: return new cl_Init_Cmd;
		case TAG_CLASS_ANSW_INIT: return new cl_Init_Answer;

		case TAG_CLASS_CMD_REG_ELEMS: return new cl_Reg_Elems_Cmd;
		case TAG_CLASS_ANSW_REG_ELEMS: return new cl_Reg_Elems_Answer;

		case TAG_CLASS_CMD_GET_DATA: return new cl_Get_Data_Cmd;
		case TAG_CLASS_ANSW_DATA: return new cl_Data_Answer;

		case TAG_CLASS_ELEM_STUB: return new cl_Elem_Stub;

		case TAG_CLASS_VALUE: return new cl_Elem_104_Value;
//		case TAG_CLASS_VALUE_SW: return new cl_SW_Elem_Value;
//		case TAG_CLASS_VALUE_GEN: return new cl_GEN_Elem_Value;
//		case TAG_CLASS_VALUE_MEAS: return new cl_MEAS_Elem_Value;

		case TAG_CLASS_REPLAY_CMD: return new cl_Replay_Command;
		case TAG_CLASS_REPLAY_ANSW: return new cl_Replay_Answr;

		case TAG_CLASS_POKE_FLT: return new cl_Float_Poke_Value;
		case TAG_CLASS_POKE_BOOL: return new cl_Bool_Poke_Value;
		case TAG_CLASS_POKE_4STATE: return new cl_4State_Poke_Value;
		case TAG_CLASS_POKE_CMD: return new cl_Poke_Command;
	};

//	wxLogDebug(wxString::Format(_("Unknown class type 0x%08X."), nTag));
	return nullptr;
}

#if !defined _VOLTAGE_CTRL_
cl_SerializableObject *CreateObjectByTag(uint32_t nTag)
{
	cl_SerializableObject * pObj = IEC104_CreateObjectByTag(nTag);
	if (pObj != nullptr)
		return pObj;

	wxLogError("cl_SerializableObject *CreateObjectByTag: Unknown class type 0x%08X.", nTag);
	return nullptr;
}
#endif

