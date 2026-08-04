/***************************************************************
 * Name:      Commands_Srv.cpp
 * Purpose:   Code for DNCors interface commands - server
 **************************************************************/

#include "wx_pch.h"
#include "Commands.h"
#include "cl_Client.h"
#include "Log.h"
#include "main.h"

void cl_Init_Cmd::Exec(cl_Cmd_Dest *pDest)
{
	cl_Client *pDst = static_cast <cl_Client*> (pDest);
	bool bOK = pDst->Setup_104(this);
	int nMode = Mode_IEC104;
	if (pDst->m_pParent->m_Config.m_bReplayMode)
		nMode = Mode_Replay;
	m_uAnswer = std::make_unique<cl_Init_Answer>(bOK, nMode);
}

void cl_Reg_Elems_Cmd::Exec(cl_Cmd_Dest *pDest)
{
	cl_Client *pClient = static_cast <cl_Client*> (pDest);

	bool bOK = true;
	if (pClient->m_p104Client == nullptr)
	{
		bOK = false;
	}
	else
	{
		for(auto pObj: m_Elems)
		{
			if ((pObj->m_bSetPoint) || ((pObj->m_nID & MAIN_104_Flag) == MAIN_104_Flag))
			{
				cl_104_Element *pElem = pClient->m_p104Client->FindElement(pObj->m_n104Addr, true, true);
				pElem->m_bPropagate = pObj->m_bPropagate;
				pElem->m_nACK_Address = pObj->m_n104_ACK_Adress;
				pElem->m_bSetPoint = true;
				pClient->m_CmdElements.insert(std::make_pair(pObj->m_nID, pElem));
				pClient->m_CmdElement_IDs.insert(std::make_pair(pElem, pObj->m_nID));
				if ((pObj->m_nID & MAIN_104_Flag) == MAIN_104_Flag)
				{
					pClient->m_p104Client->AddInterrog(pElem);
				}
				continue;
			}
			cl_104_Element *pElem = pClient->m_p104Client->FindElement(pObj->m_n104Addr, true, false);

			pElem->m_bPropagate = pObj->m_bPropagate;
			pElem->m_nACK_Address = pObj->m_n104_ACK_Adress;
			pClient->m_Elements.insert(std::make_pair(pObj->m_nID, pElem));
			pClient->m_Element_IDs.insert(std::make_pair(pElem, pObj->m_nID));
		}
	}

	// in case of not success, clear all links - data transfer is not possible
	if (!bOK)
		pClient->m_Elements.clear();

	m_uAnswer = std::make_unique<cl_Reg_Elems_Answer>(bOK);
}

void cl_Get_Data_Cmd::Exec(cl_Cmd_Dest *pDest)
{
	cl_Client *pClient = static_cast <cl_Client*> (pDest);
	if (pClient->m_p104Client == nullptr)
		return;
	std::unique_ptr<cl_Data_Answer> uDataAnsw = std::make_unique<cl_Data_Answer>();

	if (m_bStart)
		pClient->m_itElements = pClient->m_Elements.begin();

	int nCount = 0;
	while (pClient->m_itElements != pClient->m_Elements.end())
	{
		cl_104_Element *pElem = pClient->m_itElements->second;

		if (pElem->HaveNewData(m_NewerThan))
		{
			cl_Elem_104_Value *pValue = pElem->GetData();
			uDataAnsw->m_Value.push_back(std::unique_ptr<cl_Elem_104_Value>(pValue));

			if (++nCount >= ELEM_DATA_RECORDS_MAX)
				break;
		}
		pClient->m_itElements++;
	}
	uDataAnsw->m_bFinal = pClient->m_itElements == pClient->m_Elements.end();
	m_uAnswer = std::move(uDataAnsw);
}

void cl_Poke_Command::Exec(cl_Cmd_Dest *pDest)
{
	cl_Client *pClient = static_cast <cl_Client*> (pDest);
	if (pClient->m_p104Client == nullptr)
		return;

	uint64_t nID = m_nElementID;
	auto iter = pClient->m_CmdElements.find(nID);
	if (iter == pClient->m_CmdElements.end())
	{return;}

	cl_104_Element *pElem = iter->second;

	for(auto elem_iter = m_lstValue.begin(); elem_iter != m_lstValue.end(); ++elem_iter)
	{
		cl_Poke_Value *pValue = elem_iter->get();
		if (pValue->GetObjectType() == TAG_CLASS_POKE_FLT)
		{
			if (!pClient->m_pParent->m_Config.m_bReplayMode)
			{
				cl_Float_Poke_Value *pFPv = static_cast <cl_Float_Poke_Value*> (pValue);
				pClient->m_p104Client->Send_MeasuredValueShort(pElem, pFPv->m_fValue, pFPv->m_nCOT, false);
				pElem->m_fValue = pFPv->m_fValue;
				pElem->m_nType = M_ME_NC_1;
			}
		}
		else if (pValue->GetObjectType() == TAG_CLASS_POKE_BOOL)
		{
			if (!pClient->m_pParent->m_Config.m_bReplayMode)
			{
				cl_Bool_Poke_Value *pBPv = static_cast <cl_Bool_Poke_Value*> (pValue);
				pClient->m_p104Client->SinglePointInformation(pElem, pBPv->m_bValue, pBPv->m_nCOT, false);
				pElem->m_fValue = pBPv->m_bValue ? 1. : 0.;
				pElem->m_nType = M_SP_NA_1;
			}
		}
		else if (pValue->GetObjectType() == TAG_CLASS_POKE_4STATE)
		{
			if (!pClient->m_pParent->m_Config.m_bReplayMode)
			{
				cl_4State_Poke_Value *pDPv = static_cast <cl_4State_Poke_Value*> (pValue);
				pClient->m_p104Client->DoublePointInformation(pElem, pDPv->m_nValue, pDPv->m_nCOT, false);
				pElem->m_fValue = pDPv->m_nValue;
				pElem->m_nType = M_DP_NA_1;
			}
		}
	}
}

