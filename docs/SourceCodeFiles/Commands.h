/***************************************************************
 * Name:      Commands.h
 * Purpose:   Defines DNCors interface commands
 **************************************************************/

#ifndef COMMANDS_H
#define COMMANDS_H

#include <memory>
#include <list>

#include "Serializable.h"
#include "tlv_tag.h"

#if defined _IEC_104_
	#include "cs104_connection.h"
#else
	typedef enum {
		 CS101_COT_PERIODIC = 1,
		 CS101_COT_BACKGROUND_SCAN = 2,
		 CS101_COT_SPONTANEOUS = 3,
		 CS101_COT_INITIALIZED = 4,
		 CS101_COT_REQUEST = 5,
		 CS101_COT_ACTIVATION = 6,
		 CS101_COT_ACTIVATION_CON = 7,
		 CS101_COT_DEACTIVATION = 8,
		 CS101_COT_DEACTIVATION_CON = 9,
		 CS101_COT_ACTIVATION_TERMINATION = 10,
		 CS101_COT_RETURN_INFO_REMOTE = 11,
		 CS101_COT_RETURN_INFO_LOCAL = 12,
		 CS101_COT_FILE_TRANSFER = 13,
		 CS101_COT_AUTHENTICATION = 14,
		 CS101_COT_MAINTENANCE_OF_AUTH_SESSION_KEY = 15,
		 CS101_COT_MAINTENANCE_OF_USER_ROLE_AND_UPDATE_KEY = 16,
		 CS101_COT_INTERROGATED_BY_STATION = 20,
		 CS101_COT_INTERROGATED_BY_GROUP_1 = 21,
		 CS101_COT_INTERROGATED_BY_GROUP_2 = 22,
		 CS101_COT_INTERROGATED_BY_GROUP_3 = 23,
		 CS101_COT_INTERROGATED_BY_GROUP_4 = 24,
		 CS101_COT_INTERROGATED_BY_GROUP_5 = 25,
		 CS101_COT_INTERROGATED_BY_GROUP_6 = 26,
		 CS101_COT_INTERROGATED_BY_GROUP_7 = 27,
		 CS101_COT_INTERROGATED_BY_GROUP_8 = 28,
		 CS101_COT_INTERROGATED_BY_GROUP_9 = 29,
		 CS101_COT_INTERROGATED_BY_GROUP_10 = 30,
		 CS101_COT_INTERROGATED_BY_GROUP_11 = 31,
		 CS101_COT_INTERROGATED_BY_GROUP_12 = 32,
		 CS101_COT_INTERROGATED_BY_GROUP_13 = 33,
		 CS101_COT_INTERROGATED_BY_GROUP_14 = 34,
		 CS101_COT_INTERROGATED_BY_GROUP_15 = 35,
		 CS101_COT_INTERROGATED_BY_GROUP_16 = 36,
		 CS101_COT_REQUESTED_BY_GENERAL_COUNTER = 37,
		 CS101_COT_REQUESTED_BY_GROUP_1_COUNTER = 38,
		 CS101_COT_REQUESTED_BY_GROUP_2_COUNTER = 39,
		 CS101_COT_REQUESTED_BY_GROUP_3_COUNTER = 40,
		 CS101_COT_REQUESTED_BY_GROUP_4_COUNTER = 41,
		 CS101_COT_UNKNOWN_TYPE_ID = 44,
		 CS101_COT_UNKNOWN_COT = 45,
		 CS101_COT_UNKNOWN_CA = 46,
		 CS101_COT_UNKNOWN_IOA = 47
	} CS101_CauseOfTransmission;
#endif // defined

#define ID_104_Active				1
#define ID_104_RegMode				2
#define ID_104_RegBranch			3
#define ID_104_UNet_max				4
#define ID_104_UNet_min				5
#define ID_104_Qvvn					6
#define ID_104_Q_tor					7

#define ID_104_Active_ACK			101
#define ID_104_State					102
#define ID_104_Q_min					103
#define ID_104_Q_max					104
#define ID_104_Losses				105
#define ID_104_Weak					106

class cl_Cmd_Dest{
public:	cl_Cmd_Dest() { ; }
};

class cl_Command : public cl_SerializableObject
{
protected:
	uint32_t								m_nSessionID;
public:
	std::unique_ptr<cl_Command>	m_uAnswer;
public:
	cl_Command();
	uint32_t GetSessionID() { return m_nSessionID;}
	void SetSessionID(uint32_t nID) { m_nSessionID = nID;}
	virtual void Exec(cl_Cmd_Dest *pDest) { ; }
	virtual bool IsAnswer() { return false; }
	/** \brief Get type of this object - used for TLV class header
	*   \return type of this object
	*/
	virtual uint32_t GetObjectType() override { return TAG_CLASS_COMMAND; }
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

class cl_Cmd_Answer : public cl_Command
{
public:
	virtual bool IsAnswer() override { return true; }
};

class cl_Ack_Answ : public cl_Cmd_Answer
{
public:
	virtual uint32_t GetObjectType() override { return TAG_CLASS_ANSW_ACK; }
};

class cl_Init_Cmd : public cl_Command
{
public:
	wxString					m_szFile;
	wxString					m_sz104_Server;
	public:
	cl_Init_Cmd();
	cl_Init_Cmd(wxString sz104_Server);

	virtual uint32_t GetObjectType() override { return TAG_CLASS_CMD_INIT; }

	virtual void Exec(cl_Cmd_Dest *pDest) override;

	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

#define Mode_NA			-1
#define Mode_IEC104		0
#define Mode_Replay		1
class cl_Init_Answer : public cl_Cmd_Answer
{
public:
	bool						m_bInit_OK;
	uint8_t					m_nMode;

public:
	cl_Init_Answer();
	cl_Init_Answer(bool bInit_OK, uint8_t nMode);

	virtual uint32_t GetObjectType() override { return TAG_CLASS_ANSW_INIT; }

	virtual void Exec(cl_Cmd_Dest *pDest) override;

	virtual void Serialize (cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

//----------------------------------------------------------------------------
// cl_Elem_Stub - only for (de)serialization
//----------------------------------------------------------------------------
#define MAIN_104_Flag	0xC0000000
class cl_Elem_Stub : public cl_SerializableObject
{
public:
	uint32_t										m_nID;
	uint64_t										m_n104Addr;
	uint64_t										m_n104_ACK_Adress;
	uint8_t										m_n104Type;
	bool											m_bSetPoint;
	bool											m_bPropagate;

public:
	cl_Elem_Stub();
	cl_Elem_Stub(uint32_t nID, uint64_t n104Addr, bool bSetPoint = false, bool bPropagate = false);

	virtual uint32_t GetObjectType() override { return TAG_CLASS_ELEM_STUB; }
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;

	static wxString AddrToStr(uint64_t nAddress);
};

//----------------------------------------------------------------------------
// cl_Reg_Elems_Cmd - registry scheme elements to receive 104 value
//----------------------------------------------------------------------------
#define ELEM_REG_RECORDS_MAX	30
class cl_Reg_Elems_Cmd : public cl_Command
{
public:
	std::list<cl_Elem_Stub*> 		m_Elems;

public:
	cl_Reg_Elems_Cmd();
	virtual ~cl_Reg_Elems_Cmd();
	virtual uint32_t GetObjectType() override { return TAG_CLASS_CMD_REG_ELEMS; }
	virtual void Exec(cl_Cmd_Dest *pDest) override;
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
	virtual bool ProcessNewSubObject(cl_SerializableObject *pObj) override;
};

class cl_Reg_Elems_Answer : public cl_Cmd_Answer
{
public:
	bool						m_bOK;
public:
	cl_Reg_Elems_Answer();
	cl_Reg_Elems_Answer(bool bOK);
	virtual uint32_t GetObjectType() override { return TAG_CLASS_ANSW_REG_ELEMS; }
	virtual void Exec(cl_Cmd_Dest *pDest) override;
	virtual void Serialize (cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

//----------------------------------------------------------------------------
// cl_Get_Data_Cmd - get current data
//----------------------------------------------------------------------------
class cl_Get_Data_Cmd : public cl_Command
{
public:
	wxDateTime				m_NewerThan;
	bool 						m_bStart;

public:
	cl_Get_Data_Cmd();
	cl_Get_Data_Cmd(wxDateTime	NewerThan, bool bStart);
	virtual uint32_t GetObjectType() override { return TAG_CLASS_CMD_GET_DATA; }
	virtual void Exec(cl_Cmd_Dest *pDest) override;
	virtual void Serialize (cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};
//----------------------------------------------------------------------------
// cl_Elem_104_Value
//----------------------------------------------------------------------------
class cl_Elem_104_Value : public cl_SerializableObject
{
public:
	uint64_t										m_nAddress;
	wxDateTime									m_dtLastData;
	double										m_fValue;
	uint32_t										m_nQuality;
public:
	cl_Elem_104_Value();
	virtual uint32_t GetObjectType() override { return TAG_CLASS_VALUE; }
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

typedef std::unique_ptr<cl_Elem_104_Value> cl_Elem_104_Value_UPtr;

#define ELEM_DATA_RECORDS_MAX	30
class cl_Data_Answer : public cl_Cmd_Answer
{
public:
	std::list<cl_Elem_104_Value_UPtr> 		m_Value;
	bool												m_bFinal;

public:
	cl_Data_Answer();
	virtual uint32_t GetObjectType() override { return TAG_CLASS_ANSW_DATA; }
	virtual void Exec(cl_Cmd_Dest *pDest) override;
	virtual void Serialize (cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
	virtual bool ProcessNewSubObject(cl_SerializableObject *pObj) override;
};

//----------------------------------------------------------------------------
// cl_Poke_Value
//----------------------------------------------------------------------------
class cl_Poke_Value : public cl_SerializableObject
{
public:

public:
	cl_Poke_Value() { ; }
};

typedef std::unique_ptr<cl_Poke_Value> cl_Poke_Value_UPtr;

//----------------------------------------------------------------------------
// POKE VALUE CLASSES
//----------------------------------------------------------------------------
class cl_Bool_Poke_Value : public cl_Poke_Value
{
public:
	bool											m_bValue;
	uint8_t										m_nCOT;
public:
	cl_Bool_Poke_Value();
	cl_Bool_Poke_Value(bool bValue, uint8_t nCOT = CS101_COT_SPONTANEOUS)
	virtual uint32_t GetObjectType() override { return TAG_CLASS_POKE_BOOL; }
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

class cl_4State_Poke_Value : public cl_Poke_Value
{
public:
	uint8_t										m_nValue;
	uint8_t										m_nCOT;

public:
	cl_4State_Poke_Value();
	cl_4State_Poke_Value(int nValue, uint8_t nCOT = CS101_COT_SPONTANEOUS);
	virtual uint32_t GetObjectType() override { return TAG_CLASS_POKE_4STATE; }
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

class cl_Float_Poke_Value : public cl_Poke_Value
{
public:
	double										m_fValue;
	uint8_t										m_nCOT;

public:
	cl_Float_Poke_Value();
	cl_Float_Poke_Value(double fValue, uint8_t nCOT = CS101_COT_SPONTANEOUS);
	virtual uint32_t GetObjectType() override { return TAG_CLASS_POKE_FLT; }
	virtual void Serialize(cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
};

class cl_Poke_Command : public cl_Command
{
public:
	uint32_t										m_nElementID;
	std::list<cl_Poke_Value_UPtr> 		m_lstValue;

public:
	cl_Poke_Command() { ; }
	cl_Poke_Command(uint32_t nElementID);
	virtual uint32_t GetObjectType() override { return TAG_CLASS_POKE_CMD; }
	virtual void Exec(cl_Cmd_Dest *pDest) override;
	virtual void Serialize (cl_Serializer *pSerializer) override;
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) override;
	virtual bool ProcessNewSubObject(cl_SerializableObject *pObj) override;
};

cl_SerializableObject *IEC104_CreateObjectByTag(uint32_t nTag);

#endif	//COMMANDS_H

