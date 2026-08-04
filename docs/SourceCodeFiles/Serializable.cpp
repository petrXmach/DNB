/***************************************************************
 * Name:      Serializable.cpp
 * Purpose:   Code for serializable classes
 * Author:    Karel Hojdar (khojdar@egc-cb.cz)
 **************************************************************/

#ifndef NO_WX_PCH
#include "wx_pch.h"
#endif // NO_WX_PCH

#include "Serializable.h"

#if !defined _SER_NO_BZ2_
	#include <bzlib.h>
#endif

#define TLV_CLASS	                     0x80000000

// cl_Serializer
cl_Serializer::cl_Serializer(uint32_t nSize)
{
	m_nOffset = 0;
	m_nSize = nSize;

	if (m_nSize == 0)
		m_nSize = 65536;

	m_pData = new uint8_t[m_nSize];
}

cl_Serializer::~cl_Serializer()
{
	delete [] m_pData;
}

void cl_Serializer::AddTLV(uint32_t nTag, uint32_t nLength, void *pValue)
{
	EnsureSpace(sizeof(tlv_head_t) + nLength);

	tlv_head_t *tlv = (tlv_head_t *)(m_pData + m_nOffset);
	tlv->nTag = nTag;
	tlv->nLength = nLength;

	if (nLength)
		memcpy (m_pData + m_nOffset + sizeof(tlv_head_t), pValue, nLength);

	m_nOffset += sizeof(tlv_head_t) + nLength;
}

void cl_Serializer::AddTLV_UTF8(uint32_t nTag, wxString& szValue)
{
	wxCharBuffer Buff = szValue.utf8_str();
	const char *pBuff = Buff.data();
	AddTLV(nTag, (strlen(pBuff) + 1), (void *)pBuff);
}

void cl_Serializer::AddTLV_UTF16(uint32_t nTag, wxString& szValue)
{
	wxCharBuffer Buff = szValue.mb_str(wxMBConvUTF16LE());
	const char *pBuff = Buff.data();
	AddTLV(nTag, (szValue.Length() + 1) * 2, (void *)pBuff);
}

void cl_Serializer::AddTLV_Bool(uint32_t nTag, bool bValue)
{
	uint8_t nValue = bValue ? 1 : 0;
	AddTLV_U8(nTag, nValue);
}

void cl_Serializer::AddTLV_U8(uint32_t nTag, uint8_t nValue)
{
	AddTLV(nTag, sizeof(uint8_t), &nValue);
}

void cl_Serializer::AddTLV_U16(uint32_t nTag, uint16_t nValue)
{
	AddTLV(nTag, sizeof(uint16_t), &nValue);
}

void cl_Serializer::AddTLV_I16(uint32_t nTag, int16_t nValue)
{
	AddTLV(nTag, sizeof(int16_t), &nValue);
}

void cl_Serializer::AddTLV_U32(uint32_t nTag, uint32_t nValue)
{
	AddTLV(nTag, sizeof(uint32_t), &nValue);
}

void cl_Serializer::AddTLV_I32(uint32_t nTag, int32_t nValue)
{
	AddTLV(nTag, sizeof(int32_t), &nValue);
}

void cl_Serializer::AddTLV_U64(uint32_t nTag, uint64_t nValue)
{
	AddTLV(nTag, sizeof(uint64_t), &nValue);
}

void cl_Serializer::AddTLV_Dbl(uint32_t nTag, double nValue)
{
	AddTLV(nTag, sizeof(double), &nValue);
}

void cl_Serializer::AddTLV_CD(uint32_t nTag, std::complex<double> cfValue)
{
	double fComp[2];
	fComp[0] = cfValue.real();
	fComp[1] = cfValue.imag();
	AddTLV(nTag, sizeof(double) * 2, fComp);
}

void cl_Serializer::AddTLV_Date(uint32_t nTag, wxDateTime &dtValue)
{
	wxLongLong llVal = dtValue.GetValue();
	uint64_t i64Val = llVal.GetValue();
	AddTLV(nTag, sizeof(uint64_t), &i64Val);
}

#ifndef NO_GUI
	void cl_Serializer::AddTLV_Colour(uint32_t nTag, wxColour &Colour)
	{
		uint32_t nColour = Colour.GetRGBA();
		AddTLV_U32(nTag, nColour);
	}

	void cl_Serializer::GetTLV_Colour(uint32_t nLength, void *pValue, wxColour *pColour)
	{
		uint32_t nColour;
		GetTLV_U32(nLength, pValue, nColour);
		pColour->SetRGBA(nColour);
	}
#endif // NO_GUI

bool cl_Serializer::GetTLV(uint32_t &nTag, uint32_t &nLength, void *&pValue)
{
	if (m_nOffset >= m_nSize) //- are we at the end?
		return false;

	tlv_head_t *tlv = (tlv_head_t *)(m_pData + m_nOffset);
	nTag = tlv->nTag;
	nLength = tlv->nLength;
	pValue = (m_pData + m_nOffset + sizeof(tlv_head_t));
	return true;
}

void cl_Serializer::GetTLV(uint32_t nLength, void *pValue, void *pResult)
{
	memcpy(pResult, pValue, nLength);
}

void cl_Serializer::GetTLV_CD(uint32_t nLength, void *pValue, std::complex<double> *pcfValue)
{
	double *pfComp = (double*)pValue;
	pcfValue->real(*pfComp);
	pfComp++;
	pcfValue->imag(*pfComp);
}

void cl_Serializer::GetTLV_Bool(uint32_t nLength, void *pValue, bool &out)
{
	out = (*((uint8_t *)pValue) != 0);
}

void cl_Serializer::GetTLV_U8(uint32_t nLength, void *pValue, uint8_t &out)
{
	out = *((uint8_t *)pValue);
}

void cl_Serializer::GetTLV_U16(uint32_t nLength, void *pValue, uint16_t &out)
{
	out = *((uint16_t *)pValue);
}

void cl_Serializer::GetTLV_I16(uint32_t nLength, void *pValue, int16_t &out)
{
	out = *((int16_t *)pValue);
}

void cl_Serializer::GetTLV_U32(uint32_t nLength, void *pValue, uint32_t &out)
{
	out = *((uint32_t *)pValue);
}

void cl_Serializer::GetTLV_I32(uint32_t nLength, void *pValue, int32_t &out)
{
	out = *((int32_t *)pValue);
}

void cl_Serializer::GetTLV_U64(uint32_t nLength, void *pValue, uint64_t &out)
{
	out = *((uint64_t *)pValue);
}

void cl_Serializer::GetTLV_Dbl(uint32_t nLength, void *pValue, double &out)
{
	out = *((double *)pValue);
}

void cl_Serializer::GetTLV_Date(uint32_t nLength, void *pValue, wxDateTime &out)
{
	wxUint64 Val;
	Val = *((wxUint64 *)pValue);
	out = wxDateTime(wxLongLong(Val));
}

void cl_Serializer::GetTLV_UTF8(uint32_t nLength, void *pValue, wxString &out)
{
	out = wxString::FromUTF8((const char*)pValue, nLength - 1);
}

void cl_Serializer::GetTLV_UTF16(uint32_t nLength, void *pValue, wxString &out)
{
	out = wxString((const char*)pValue, wxMBConvUTF16LE());
}

void cl_Serializer::Serialize(cl_SerializableObject *pObj)
{
	uint32_t nOffset = m_nOffset; //- mark actual positon...

	//- TLV's length will be computed later
	AddTLV(pObj->GetObjectType(), 0, 0);
	pObj->Serialize(this); //- serialize all my attributes

	//- compute and correct TLV's length (using marked position)
	tlv_head_t *tlv = (tlv_head_t *)(m_pData + nOffset);
	tlv->nLength = m_nOffset - nOffset - sizeof(tlv_head_t);
}

cl_SerializableObject *cl_Serializer::Deserialize()
{
	uint32_t nTag, nLength;
	void *pValue;

	m_nOffset = 0;//- we have to start at the begin of array

	//- there is always at least one class
	if (GetTLV (nTag, nLength, pValue))
	{
		cl_SerializableObject *pRoot = CreateObjectByTag(nTag);
		if (pRoot == NULL) //- uknown class
			return NULL;

		SkipTLVHead ();
		DeserializeClassRecursive(pRoot, nLength);
		try
		{
			pRoot->Deserialize_Done();
		}
		catch (...)
		{
			delete pRoot;
			throw;
		}
		return pRoot;
	}

	return NULL;
}

void cl_Serializer::DeserializeClassRecursive(cl_SerializableObject *pObj, uint32_t nBaseLength)
{
	uint32_t nTag, nLength;
	void *pValue;
	nBaseLength += m_nOffset; //- absolute end of data

	while ((m_nOffset < nBaseLength) && GetTLV(nTag, nLength, pValue))
	{
		if ((nTag & TLV_CLASS) == TLV_CLASS)
		{
			cl_SerializableObject *pNew = pObj->CreateObjectByTag(nTag);
			if (pNew != NULL)
			{
				SkipTLVHead();
				DeserializeClassRecursive(pNew, nLength);
				pNew->Deserialize_Done();

				if (!pObj->ProcessNewSubObject(pNew))
					delete pNew;
			}
			else //- Unknown class type, ignore
				SkipTLV();
		}
		else //- deserialize one attribute
		{
			pObj->Deserialize(nTag, nLength, pValue);
			SkipTLV();
		}
	}
}

void cl_Serializer::EnsureSpace(uint32_t nSize)
{
	if (m_nSize < (m_nOffset + nSize))
	{ //- there is not enough space in buffer
		uint32_t new_size = (m_nOffset + nSize + 65536) & ~4095; //- expand it +64KB, page aligned
		uint8_t *pData = new uint8_t[new_size];
		memcpy (pData, m_pData, m_nSize);
		delete [] m_pData;
		m_pData = pData;
		m_nSize = new_size;
	}
}

void cl_Serializer::SkipTLV()
{
	tlv_head_t *tlv = (tlv_head_t *)(m_pData + m_nOffset);
	m_nOffset += tlv->nLength + sizeof (tlv_head_t);
}

void cl_Serializer::SkipTLVHead()
{
	m_nOffset += sizeof(tlv_head_t);
}

bool cl_Serializer::WriteTLVArchive (wxFile &OFile, int nCompressionLevel)
{
	uint32_t nSize;
	uint8_t *pBuffer;
	bool bOK;

	tlv_head_t Head;
	bOK = (OFile.Write(m_pData, m_nOffset) == m_nOffset);
	return bOK;
}

uint32_t cl_Serializer::WriteTLVArchive(uint8_t **pBuffer, int nCompressionLevel)
{
	*pBuffer = NULL;

	tlv_head_t Head;

	uint32_t nSize;
	uint8_t *pTmpBuffer;

		pTmpBuffer = m_pData;
		nSize = m_nOffset;
		Head.nTag = 0;

	Head.nLength = nSize;

	*pBuffer = (uint8_t*)malloc(nSize + sizeof(Head));
	memcpy(*pBuffer, &Head, sizeof(Head));
	memcpy((*pBuffer) + sizeof(Head), pTmpBuffer, nSize);

	return nSize + sizeof(Head);
}

void cl_Serializer::ReadTLV(wxFile *IFile, uint8_t *pBuffer, uint32_t nBuffLen)
{
	tlv_head_t head;
	char *buffer = NULL;

	uint32_t nBuffPos = 0;
	assert((IFile != NULL) || (pBuffer != NULL));

	try
	{
		if (IFile != NULL)
		{
			if ((sizeof(head)) != IFile->Read (&head, sizeof (head)))
				throw new BASE_EXCEPTION(_("cl_Serializer::ReadTLV chyba při čtení hlavičky archivu."));
		}
		else
		{
			assert ((nBuffPos + sizeof (head)) <= nBuffLen);
			memcpy(&head, pBuffer + nBuffPos, sizeof (head));
			nBuffPos += sizeof (head);
		}

		m_nOffset = 0;
		//- archive is compressed
		if (head.nTag == COMPRESSED_ARCHIVE_MAGIC)
		{
			#if !defined _SER_NO_BZ2_
				//- we dont know how much space should we alloc, so we will make a guess
				EnsureSpace(head.nLength);

				char *buffer = new char[head.nLength];
				if (buffer == nullptr)
					throw new BASE_EXCEPTION(_("cl_Serializer::ReadTLV nedostatek paměti."));

				if (IFile != nullptr)
				{
					if (((int)head.nLength) != IFile->Read(buffer, head.nLength))
						throw new BASE_EXCEPTION(_("cl_Serializer::ReadTLV chyba při čtení archivu."));
				}
				else
				{
					assert ((nBuffPos + head.nLength) <= nBuffLen);
					memcpy(buffer, pBuffer + nBuffPos, head.nLength);
					nBuffPos += head.nLength;
				}

				bz_stream stream = {0};

				stream.next_in = buffer;
				stream.avail_in = head.nLength;
				stream.next_out = (char*)m_pData;
				stream.avail_out = m_nSize;

				if (BZ2_bzDecompressInit (&stream, 0, 0) != 0)
					throw new BASE_EXCEPTION(_("cl_Serializer::ReadTLV chyba dekomprese.\nPravděpodobně nesoulad BZ2 knihoven."));

				int rv;
				while (1)
				{
					rv = BZ2_bzDecompress (&stream);
					m_nOffset = stream.total_out_lo32;
					if (rv != BZ_OK)
						break;

					//- we need bigger output buffer
					if (stream.avail_out == 0)
					{
						EnsureSpace(4096);
						stream.next_out = (char *)m_pData + m_nOffset;
						stream.avail_out = m_nSize - m_nOffset;
					}
				}

				BZ2_bzDecompressEnd (&stream);
				delete[] buffer;
				buffer = nullptr;

				if (rv == BZ_STREAM_END)
					return;
			#endif
			m_nOffset = 0;
			throw new BASE_EXCEPTION(_("cl_Serializer::ReadTLV chyba při dekompresi archivu.\nPravděpodobné porušení integrity dat."));
		}
		else
		{ //- plain TLV
			EnsureSpace(head.nLength + sizeof(head));
			memcpy (m_pData, &head, sizeof(head));
			if (head.nLength > 0)
			{
				if (IFile != NULL)
				{
					if (((int)head.nLength) != IFile->Read(m_pData + sizeof (head), head.nLength))
						throw new BASE_EXCEPTION(_("cl_Serializer::ReadTLV chyba při čtení archivu."));
				}
				else
				{
					assert((nBuffPos + head.nLength) < nBuffLen);
					memcpy(m_pData, pBuffer + nBuffPos, head.nLength);
					nBuffPos += head.nLength;
				}
			}
			m_nOffset = head.nLength + sizeof(head);
		}
	}
	catch (...)
	{
		if (buffer != NULL)
			delete[] buffer;
		throw;
	}
}



