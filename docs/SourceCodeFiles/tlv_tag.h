/***************************************************************
 * Name:      tlv_tag.h
 * Purpose:   Defines tags for serialization engine
 * Author:    EGC CB s.r.o. (khojdar@egc-cb.cz)
 * Created:   2014-02-26
 * Copyright: EGC CB s.r.o. (www.egc-cb.cz)
 * License:
 **************************************************************/

#ifndef TLV_H_INCLUDED
#define TLV_H_INCLUDED

#define TLV_CLASS	                     0x80000000
#define CLASS(tag) 	(tag | TLV_CLASS)

#include "DN_Cors_tag.h"

#define	TAG_u32_ID							0x00000001
#define	TAG_sz8_Name						0x00000002

#endif // TLV_H_INCLUDED


