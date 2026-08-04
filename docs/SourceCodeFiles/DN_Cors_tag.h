
/***************************************************************
 * Name:      Auth_tlv_tag.h
 * Purpose:   Defines tags for authentification engine
 * Author:    Cm ()
 * Created:   2019-04-02
 * Copyright: Karel Hojdar (www.egc-cb.cz)
 * License:
 **************************************************************/

#ifndef AUTH_TLV_H_INCLUDED
#define AUTH_TLV_H_INCLUDED

//************** Basic commands ************
#define TAG_CLASS_COMMAND						CLASS(0x00101000)
#define TAG_CLASS_ANSW_ACK						CLASS(0x00101002)
#define TAG_CLASS_CMD_QUIT						CLASS(0x00101004)
#define TAG_CLASS_CMD_QUIT_ACK				CLASS(0x00101005)

#define TAG_CLASS_CMD_INIT						CLASS(0x00101010)
#define TAG_CLASS_ANSW_INIT					CLASS(0x00101011)

#define TAG_CLASS_CMD_REG_ELEMS				CLASS(0x00101012)
#define TAG_CLASS_ANSW_REG_ELEMS				CLASS(0x00101013)

#define TAG_CLASS_CMD_GET_DATA				CLASS(0x00101014)
#define TAG_CLASS_ANSW_DATA					CLASS(0x00101015)

#define TAG_CLASS_REPLAY_CMD					CLASS(0x00101016)
#define TAG_CLASS_REPLAY_ANSW					CLASS(0x00101017)

#define TAG_CLASS_POKE_CMD						CLASS(0x00101018)

#define TAG_u32_Client_ID							0x00110001
//#define TAG_sz8_Name									0x00110002
#define TAG_sz8_IP									0x00110003
#define TAG_u16_Port									0x00110004
#define TAG_sz8_Server								0x00110005
#define TAG_b_OK										0x00110006
//#define TAG_u32_ID									0x00110007
#define TAG_dt_DateTime								0x00110008
#define TAG_b_Value									0x00110009
#define TAG_dbl_Value_U								0x0011000A
#define TAG_dbl_Value_P								0x0011000B
#define TAG_dbl_Value_Q								0x0011000C
#define TAG_u8_Mode									0x0011000D
#define TAG_u32_Value								0x0011000E
#define TAG_dt_From									0x00110010
#define TAG_dt_To										0x00110011
#define TAG_dbl_Value								0x00110010
#define TAG_u64_104ADDR								0x00110012
#define TAG_b_SetPoint								0x00110013
#define TAG_u32_Quality								0x00110014
#define TAG_b_Propagate								0x00110015
#define TAG_u64_ACK_104ADDR						0x00110016
#define TAG_u8_COT									0x00110017
#define TAG_u8_104TYPE								0x00110018
#define TAG_u8_Value									0x00110019

#define TAG_CLASS_ELEM_STUB					CLASS(0x00102000)
#define TAG_CLASS_VALUE_SW						CLASS(0x00102001)
#define TAG_CLASS_VALUE_GEN					CLASS(0x00102002)
#define TAG_CLASS_VALUE_MEAS					CLASS(0x00102003)
#define TAG_CLASS_VALUE_NODE					CLASS(0x00102004)
#define TAG_CLASS_VALUE_TRANSF				CLASS(0x00102005)
#define TAG_CLASS_VALUE							CLASS(0x00102006)

#define TAG_CLASS_POKE_FLT						CLASS(0x00102010)
#define TAG_CLASS_POKE_BOOL					CLASS(0x00102011)
#define TAG_CLASS_POKE_4STATE					CLASS(0x00102012)

#endif

