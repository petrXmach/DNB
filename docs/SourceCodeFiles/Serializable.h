/***************************************************************
 * Name:      Serializable.h
 * Purpose:   Defines for serializable classes
 **************************************************************/

#ifndef SERIALIZABLE_H_INCLUDED
#define SERIALIZABLE_H_INCLUDED

#include <wx/file.h>
#include <complex>

#include <wx/datetime.h>

#ifndef NO_GUI
	#include <wx/colour.h>
#endif // NO_GUI

#include "common.h"
#include "Exceptions.h"

//----------------------------------------------------------------------------
// cl_Serializer
//----------------------------------------------------------------------------

class cl_SerializableObject;

/** \brief Create object with the given tag. Global version - used in top level deserialization.
*   \param nTag tag of the newly created object
*   \return created object
*/
cl_SerializableObject *CreateObjectByTag(uint32_t nTag);

//----------------------------------------------------------------------------
// cl_Serializer
//----------------------------------------------------------------------------
/**
* \brief Performs serialization & de-serialization of the object model.
*/
class cl_Serializer
{
public:
	static const uint32_t COMPRESSED_ARCHIVE_MAGIC = 	0x564C5458;
	/**< compressed TLV stream header 'VLTX' */

private:
	uint8_t 			*m_pData;
	/**< stream buffer */
	uint32_t 		m_nOffset;
	/**< actual offset in the stream buffer */
	uint32_t 		m_nSize;
	/**< actual size of the stream buffer */

public:
	cl_Serializer(uint32_t nSize = 65536);

	~cl_Serializer();

	void AddTLV(uint32_t nTag, uint32_t nLength, void *pValue);
	void AddTLV_UTF8(uint32_t nTag, wxString& szValue);
	void AddTLV_UTF8_C(uint32_t nTag, wxString szValue) { AddTLV_UTF8(nTag, szValue); }
	void AddTLV_UTF16(uint32_t nTag, wxString& szValue);
	void AddTLV_Bool(uint32_t nTag, bool bValue);
	void AddTLV_U8(uint32_t nTag, uint8_t nValue);
	void AddTLV_U16(uint32_t nTag, uint16_t nValue);
	void AddTLV_I16(uint32_t nTag, int16_t nValue);
	void AddTLV_U32(uint32_t nTag, uint32_t nValue);
	void AddTLV_I32(uint32_t nTag, int32_t nValue);
	void AddTLV_U64(uint32_t nTag, uint64_t nValue);
	void AddTLV_Dbl(uint32_t nTag, double nValue);
	void AddTLV_CD(uint32_t nTag, std::complex<double> cfValue);
	void AddTLV_Date(uint32_t nTag, wxDateTime &dtValue);
	bool GetTLV(uint32_t &nTag, uint32_t &nLength, void *&pValue);
	static void GetTLV(uint32_t nLength, void *pValue, void *pResult);
	static void GetTLV_CD(uint32_t nLength, void *pValue, std::complex<double> *pcfValue);
	static void GetTLV_Bool(uint32_t nLength, void *pValue, bool &out);
	static void GetTLV_U8(uint32_t nLength, void *pValue, uint8_t &out);
	static void GetTLV_U16(uint32_t nLength, void *pValue, uint16_t &out);
	static void GetTLV_I16(uint32_t nLength, void *pValue, int16_t &out);
	static void GetTLV_U32(uint32_t nLength, void *pValue, uint32_t &out);
	static void GetTLV_I32(uint32_t nLength, void *pValue, int32_t &out);
	static void GetTLV_U64(uint32_t nLength, void *pValue, uint64_t &out);
	static void GetTLV_Dbl(uint32_t nLength, void *pValue, double &out);
	static void GetTLV_Date(uint32_t nLength, void *pValue, wxDateTime &out);
	static void GetTLV_UTF8(uint32_t nLength, void *pValue, wxString &out);
	static void GetTLV_UTF16(uint32_t nLength, void *pValue, wxString &out);


	/** \brief Serialize an object to the buffer
	*   \param pObj serialized object
	*/
	void Serialize(cl_SerializableObject *pObj);

	/** \brief De-serialize an object from the buffer
	*   \return de-serialized object (dynamically created), NULL in case of error
	*/
	cl_SerializableObject *Deserialize();

	/** \brief Get actual size of the buffer
	*   \return actual size of the buffer
	*/
	uint32_t GetSize() const { return m_nOffset; }

	/** \brief Get data from TLV file
	*   \param IFile file with data
	*   \param pBuffer buffer with data. Used if IFile == NULL.
	*   \param nBuffLen length of the data
	*/
	void ReadTLV (wxFile *IFile, uint8_t *pBuffer = NULL, uint32_t nBuffLen = 0);

	bool WriteTLVArchive(wxFile &OFile, int nCompressionLevel = 6);
	uint32_t WriteTLVArchive(uint8_t **pBuffer, int nCompressionLevel = 6);

	/** \brief Create object with the given tag.
	*   \param nTag tag of the newly created object
	*   \return created object
	*/
	virtual cl_SerializableObject *CreateObjectByTag(uint32_t nTag) { return ::CreateObjectByTag(nTag); }

	#ifdef _MSC_VER
		#pragma pack(push,1)
		typedef struct
		{
			uint32_t nTag;
			uint32_t nLength;
		} tlv_head_t;
		#pragma pack(pop)
	#else
		typedef struct __attribute__((packed))
		{
			uint32_t nTag;
			uint32_t nLength;
		} tlv_head_t;
	#endif // _MSC_VER

protected:
	/** \brief Ensure that buffer is big enough to hold additional data
	*   \param nSize size of the additional data
	*/
	void EnsureSpace(uint32_t nSize);

	/** \brief De-serialize attributes of the given object and possible sub-objects
	*   \param pObj de-serialized object
	*   \param nBaseLength length of the related data
	*/
	void DeserializeClassRecursive(cl_SerializableObject *pObj, uint32_t nBaseLength);

	/** \brief Skip one TLV record in the buffer
	*/
	void SkipTLV();

	/** \brief Skip one TLV record header in the buffer
	*/
	void SkipTLVHead();
};

// cl_SerializableObject
/**
* \brief Interface to the serializable object
*/
class cl_SerializableObject
{
public:
	/** \brief Default constructor
	*/
	cl_SerializableObject() { ; }

	/** \brief Default destructor
	*/
	virtual ~cl_SerializableObject() { ; }

	/** \brief Get type of this object - used for TLV class header
	*   \return type of this object
	*/
	virtual uint32_t GetObjectType() = 0;

	/** \brief Handle a new sub-object de-serialization (creation)
	*   \param pObj newly instated object
	*   \return true if solved, if false pObj has to be handled or deleted
	*/
	virtual bool ProcessNewSubObject(cl_SerializableObject *pObj) { return false; }

	/** \brief Serialize object via given serializer
	*   \param pSerializer destination of the operation
	*/
	virtual void Serialize(cl_Serializer *pSerializer) = 0;

	/** \brief De-serialize particular attribute
	*   \param nTag attribute's tag
	*   \param nLength length of the data
	*   \param pValue attribute's value
	*   \return true if tag was found and processed
	*/
	virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) = 0;

	/** \brief De-serialization done. Perform init actions if required.
	*/
	virtual void Deserialize_Done() { ; }

	/** \brief Create object with the given tag.
	*   \param nTag tag of the newly created object
	*   \return created object
	*/
	virtual cl_SerializableObject *CreateObjectByTag(uint32_t nTag) { return ::CreateObjectByTag(nTag); }
};

#endif //SERIALIZABLE_H_INCLUDED
