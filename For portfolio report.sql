-- ACCOUNT Detail

select a.accnt_no m_acno, c.ac_name1 acnm, decode(c.accnt_stat,'A','Active','C','Closed','S','Suspend') ac_stat, 
0 con_no, b.comp_nm cmp_nm, b.comp_cd cmp_cd, b.avg_rt m_mpr, c.bo_id,
d.cur_bal - nvl(d.interest,0)  m_bal,
decode(b.cds,'Y','CDS',null) type, decode(nvl(pmargin,0),100,'Nonmarginable','Marginable') margin_nonmargin, invest.PURCHASE_POWER(a.accnt_no) pp,
invest.capital_gain_account_wise(a.accnt_no,sysdate,1) c_gain, 
invest.capital_gain_account_wise(a.accnt_no,sysdate,2) c_loss, b.pe_ratio
from invest.inv_portfolio a ,invest.comp b,invest.inv_profile c,
invest.dprofile d
where  a.accnt_no in (:p_1 )
and a.comp_cd=b.comp_cd
and a.accnt_no=c.accnt_no
and a.accnt_no=d.accnt_no
union
select a.accnt_no m_acno, c.ac_name1 acnm, decode(c.accnt_stat,'A','Active','C','Closed','S','Suspend') ac_stat, 0 con_no,b.comp_nm cmp_nm, b.comp_cd cmp_cd, b.avg_rt m_mpr,  c.bo_id,
d.cur_bal - nvl(d.interest,0)  m_bal,
decode(b.cds,'Y','CDS',null) type, decode(nvl(pmargin,0),100,'Nonmarginable','Marginable') margin_nonmargin, invest.PURCHASE_POWER(a.accnt_no) pp,
invest.capital_gain_account_wise(a.accnt_no,sysdate,1) c_gain, 
invest.capital_gain_account_wise(a.accnt_no,sysdate,2) c_loss,  b.pe_ratio
from invest.inv_portf_cds a ,invest.comp b,invest.inv_profile c,
invest.dprofile d
where  a.accnt_no in  (:p_1 )
and a.comp_cd=b.comp_cd
and a.accnt_no=c.accnt_no
and a.accnt_no=d.accnt_no
and a.bal_shares >0
order by 1, 3


-- Portfolio

select a.accnt_no, b.comp_cd, a.bal_shares, a.tot_pur_cost,  (a.bal_shares* a.tot_pur_cost) p_cst, 0 cds_shr, 0 cds_cost
from invest.inv_portfolio a ,invest.comp b,invest.inv_profile c,
invest.dprofile d
where  a.accnt_no in  (:p_1 )
and a.comp_cd=b.comp_cd
and a.accnt_no=c.accnt_no
and a.accnt_no=d.accnt_no
union
select a.accnt_no,  b.comp_cd ,  0 bal_shares,
 0 tot_pur_cost,  (a.bal_shares* a.tot_pur_cost) p_cst,
  a.bal_shares cds_shr,  a.tot_pur_cost cds_cost
from invest.inv_portf_cds a ,invest.comp b,invest.inv_profile c,
invest.dprofile d
where  a.accnt_no in  (:p_1 )
and a.comp_cd=b.comp_cd
and a.accnt_no=c.accnt_no
and a.accnt_no=d.accnt_no
and a.bal_shares >0
order by 1


-- rcv_amt_d

select a.accnt_no,c.comp_cd comp_cd_d, c.comp_nm comp_nm_d, 
sum(a.no_shares) hold_shr_d, a.rate rate_d, sum(a.ramount) rcv_amt_d
from invest.brdd a, invest.comp  c
where a.accnt_no=:m_acno
and a.br_cd=1
and a.tr_cd in ('IDIV','FDIV','BNC')
and a.posted='R'
and c.comp_cd=a.comp_cd
group by a.accnt_no,c.comp_cd, c.comp_nm,a.rate


1=select (to_char(ratio1,'0.00')||' :'||to_char(ratio2,'0.00')) loan_ratio from invest.inv_type

2=select nvl(sum(amount),0) margin_deposit from invest.fin_view77to where accnt_no=:m_acno and tr_cd in ('ID','AD','ITF','PQI','RPAY') and dr_cr='C'

3=0

4=select nvl(sum(amount),0) collection_in_transit from invest.trans_ord where accnt_no=:m_acno and tr_cd in ('AD')

5 = 2+3+4



6 = fund_withdraw = select nvl(sum(amount),0) fund_withdraw from invest.trans_ord where accnt_no=:accnt_no and tr_cd in ('WF') and dr_cr='D';
7 = fund_withdraw_sec = 0
8 = fund_withdraw_order = select nvl(sum(amount),0) fund_withdraw_order from invest.fin_view77to where accnt_no=:accnt_no and tr_cd in ('WF','ITF') and dr_cr='D';
10 = 6+7+8

11 = 5 - 10

urgain = select sum(ur_gain) urgain
  from invest.v_portfolio_all
  where accnt_no=:m_acno
  and ur_gain>0;
  
urloss = select sum(ur_gain) into urloss
  from invest.v_portfolio_all
  where accnt_no=:m_acno
  and ur_gain<0;


realized_gain = c_gain
realized_loss = c_loss
net_gain_loss = c_gain + c_loss

unrealized_gain = urgain
unrealized_loss = urloss
net_ugain_uloss = urgain + urloss

Total Gain/Loss = net_gain_loss - net_ugain_uloss

geAppliedInterest = select abs(nvl(sum(amount),0)) applied_interest from invest.fin_view 
					where accnt_no=:accnt_no and tr_cd in ('INT','DMT','CFS','CSTD') 
					and dr_cr='D' and vouch_dt>='01-jul-2022';
geAccuredInterest = select nvl(interest,0) accured_interest from invest.dprofile where accnt_no=:accnt_no;

Total = applied_interest + accured_interest

geEarnedDivIncome = select nvl(sum(amount),0) earned_div_income from invest.fin_view where accnt_no=:accnt_no and tr_cd in ('FDIV','IDIV','BNS') and dr_cr='C' and vouch_dt>='01-jul-2022';
  
Total_All = net_gain_loss+ net_ugain_uloss + Total Gain/Loss + applied_interest + accured_interest + Total


---- 3 ------------
mautured_fund_balance = m_bal
geReceivableFund = SELECT NVL(SUM(AMOUNT),0) sale_recv_amt FROM INVEST.TRANS_SP where tr_cd='SL' and accnt_no=:accnt_no;
Ledger balance = mautured_fund_balance + accured_interest
geIPOBlockBalance = select nvl(sum(decode(dr_cr,'D',-amount,'C',amount,0)),0) ipobbal from invest.block_ipo where accnt_no=:accnt_no;
accured_interest = accured_interest
IPO_block_Balance_Total = IPO_block_Balance + accured_interest


Receivable_Dividend = select nvl(sum(ramount),0) dividend_receivable
  from invest.brdd
  where accnt_no=:accnt_no
  and br_cd=1
  and tr_cd in ('IDIV','FDIV','BNC')
  and posted='R';
  

Equity/Asset Value (All)  = select sum(m_val) asset_val_all
  from invest.v_portfolio_all
  where accnt_no=:accnt_no;
  
Equity/Asset Value (Marginable) = select sum(m_val) asset_val_margin
  from invest.v_portfolio_all
  where accnt_no=:accnt_no 
  and margin_nonmargin='M';
  
Client's Marginable (All) = 
Client's Margin (Marginable) = 

PURCHASE_POWER = 
Equity to Debt Ration = 
  

Average_cost =Round((Int(Int(Fields!p_cst.Value) + Int(Fields!cds_cost.Value))) /  (Int(Int(Fields!bal_shares.Value) + Int(Fields!cds_shr.Value))),2)
   
Total_Cost = =Int(Int(Fields!p_cst.Value) + Int(Fields!cds_cost.Value))
Total_Cost = =Int(Int(Fields!p_cst.Value) + Int(Fields!cds_cost.Value))

Market_value = =Int(Fields!m_mpr.Value) * ( Int(Int(Fields!bal_shares.Value) + Int(Fields!cds_shr.Value)) )


Unrealized Gain/(Loss) = market VALUE - total cost

% of Gain/(Loss) = 

PE Ratio

MArKEt_value=Round(Int(Fields!m_mpr.Value) * ( Int(Int(Fields!bal_shares.Value) + Int(Fields!cds_shr.Value)))- (Int(Int(Fields!p_cst.Value) + Int(Fields!cds_cost.Value))))


market_value_up = CDec(CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value))

unrealized_gain_loss = (CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value)) - (CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))

= CDec((Int(Fields!bal_shares.Value) + Int(Fields!cds_shr.Value)) * CDec(Fields!m_mpr.Value)) - CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value))




IIF( ( Int(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)) = 0 ),
SUM((((CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value)) - (CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))) / (CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))* 100), 100
)

IIF( ( Len(Int(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value))) = 0 ),
SUM((((CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value)) - (CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))) / (CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))* 100), 100
)




=iif(  (Int(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))=0,
SUM((((CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value)) - (CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))) / (CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))* 100)
,
100 
)


-------------------



=SUM((((CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value)) - (CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))) / (CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))* 100)


=CDec( (Int(ReportItems!unrealizedGainLossUp.Value)) / (Int(ReportItems!total_cost_up.Value)) )

=CDec(Int(IIF(ReportItems!unrealizedGainLossUp.Value="", 0 , ReportItems!unrealizedGainLossUp.Value)) 
/ Int(IIF(ReportItems!total_cost_up.Value="", 0 , ReportItems!total_cost_up.Value)))


=IIF(ReportItems!total_cost_up.Value="0.00", 100.0 ,  
CDec((CDec(ReportItems!unrealizedGainLossUp.Value)) / (CDec(ReportItems!total_cost_up.Value))) 
)


=IIF(ReportItems!total_cost_up.Value="0", 100 ,  CDec((CDec(ReportItems!unrealizedGainLossUp.Value)) / (CDec(ReportItems!total_cost_up.Value))) )

=CDec( (CDec(ReportItems!subTotalunrealizedGainLossUp.Value)) / (CDec(ReportItems!subTotaltotal_cost_up.Value)) )
=CDec( (CDec(ReportItems!grandTotalunrealizedGainLossUp.Value)) / (CDec(ReportItems!grandTotaltotal_cost_up.Value)) )

unrealizedGainLossUp = =(CDec(Int(Fields!bal_shares.Value + Int(Fields!cds_shr.Value))) * CDec(Fields!m_mpr.Value)) - (CDec(CDec(Fields!p_cst.Value) + CDec(Fields!cds_cost.Value)))



CF_imbal = select nvl(cur_bal,0) mbal from invest.dprofile where accnt_no=:m_acno; 
 