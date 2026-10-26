;;; RevSync.lsp
;;; Keeps the revision number in revision triangle blocks the same as the
;;; revision attribute in the title block.
;;;
;;; Commands:
;;;   REVSYNCSETUP - pick the rev number in a triangle and in the title block
;;;                  (one-off; remembered between sessions)
;;;   REVSYNC      - sync all triangles now
;;;   REVSYNCON    - turn automatic syncing on (default when loaded)
;;;   REVSYNCOFF   - turn automatic syncing off
;;;
;;; Automatic syncing runs before SAVE / PLOT / PUBLISH and after attribute
;;; edits, COPY, INSERT and paste, so new triangles pick up the value too.
;;;
;;; Rules:
;;;   - Triangles on a layout take the revision from the title block on that layout.
;;;   - Triangles in model space take the revision from the current layout's
;;;     title block, or, when you are on the Model tab, from the title blocks
;;;     on the layouts if they all agree.

(vl-load-com)

;;; ---------------------------------------------------------------------------
;;; Settings (stored with SETENV so they survive restarts)

(defun RevSync:get (key default / v)
  (setq v (getenv (strcat "RevSync_" key)))
  (if (and v (/= v "")) v default)
)

(defun RevSync:titleTag () (strcase (RevSync:get "TitleTag" "TTW_STDCOUNTRY_DRGSHT_ATT_REVISION")))
(defun RevSync:triBlock () (strcase (RevSync:get "TriBlock" "")))
(defun RevSync:triTag ()   (strcase (RevSync:get "TriTag" "")))

(setq *RevSync-BeforeCmds* '("QSAVE" "SAVE" "SAVEAS" "PLOT" "PUBLISH" "ETRANSMIT" "EXPORTPDF" "EXPORTLAYOUT"))
(setq *RevSync-AfterCmds*  '("EATTEDIT" "ATTEDIT" "-ATTEDIT" "ATTIPEDIT" "DDATTE" "ATTSYNC" "BATTMAN"
                             "INSERT" "-INSERT" "CLASSICINSERT" "COPY" "PASTECLIP" "PASTEORIG"
                             "MIRROR" "ARRAY" "ARRAYCLASSIC" "LAYOUT" "REGEN" "REGENALL"))

;;; ---------------------------------------------------------------------------
;;; Helpers

(defun RevSync:effName (blk / n)
  (setq n (vl-catch-all-apply 'vla-get-EffectiveName (list blk)))
  (if (vl-catch-all-error-p n) (vla-get-Name blk) n)
)

(defun RevSync:attribs (blk)
  (if (= :vlax-true (vla-get-HasAttributes blk))
    (vlax-invoke blk 'GetAttributes)
  )
)

(defun RevSync:findAtt (blk tag / found)
  (foreach a (RevSync:attribs blk)
    (if (and (null found) (= (strcase (vla-get-TagString a)) tag))
      (setq found a)
    )
  )
  found
)

(defun RevSync:triangle-p (blk / name)
  (setq name (RevSync:triBlock))
  (and (/= name "") (wcmatch (strcase (RevSync:effName blk)) name))
)

(defun RevSync:allSame (lst)
  (vl-every '(lambda (x) (= x (car lst))) lst)
)

;;; Returns a list of (layoutName isModel revValue triangles) for every layout.
(defun RevSync:scan (doc / data rev tris att)
  (vlax-for lay (vla-get-Layouts doc)
    (setq rev nil tris nil)
    (vlax-for obj (vla-get-Block lay)
      (if (= (vla-get-ObjectName obj) "AcDbBlockReference")
        (if (RevSync:triangle-p obj)
          (setq tris (cons obj tris))
          (if (and (null rev) (setq att (RevSync:findAtt obj (RevSync:titleTag))))
            (setq rev (vla-get-TextString att))
          )
        )
      )
    )
    (setq data (cons (list (vla-get-Name lay) (= :vlax-true (vla-get-ModelType lay)) rev tris) data))
  )
  data
)

;;; Sets the triangle's rev attribute. Returns 1 if it changed, else 0.
(defun RevSync:setTri (tri val / att tag)
  (setq tag (RevSync:triTag))
  (setq att (if (= tag "") (car (RevSync:attribs tri)) (RevSync:findAtt tri tag)))
  (if (and att (/= (vla-get-TextString att) val)
           (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-put-TextString (list att val)))))
    1
    0
  )
)

;;; Returns (changedCount . warningString-or-nil)
(defun RevSync:sync (doc / data revs cur modelRev n val warn)
  (setq data (RevSync:scan doc)
        n    0
        revs (vl-remove nil (mapcar 'caddr (vl-remove-if 'cadr data)))
        cur  (assoc (getvar "CTAB") data)
  )
  (setq modelRev
    (cond
      ((and cur (not (cadr cur)) (caddr cur)) (caddr cur))
      ((and revs (RevSync:allSame revs)) (car revs))
    )
  )
  (foreach d data
    (setq val (if (cadr d) modelRev (caddr d)))
    (cond
      (val (foreach tri (cadddr d) (setq n (+ n (RevSync:setTri tri val)))))
      ((and (cadr d) (cadddr d) revs)
       (setq warn "Model space triangles skipped: layouts have different revisions. Switch to a layout tab and run REVSYNC."))
    )
  )
  (if (null revs)
    (setq warn (strcat "No title block with attribute " (RevSync:titleTag) " found."))
  )
  (cons n warn)
)

(defun RevSync:run ()
  (if (/= (RevSync:triBlock) "")
    (vl-catch-all-apply 'RevSync:sync (list (vla-get-ActiveDocument (vlax-get-acad-object))))
  )
)

;;; ---------------------------------------------------------------------------
;;; Reactors

(defun RevSync:onWillStart (r args)
  (if (member (strcase (car args)) *RevSync-BeforeCmds*) (RevSync:run))
)

(defun RevSync:onEnded (r args)
  (if (member (strcase (car args)) *RevSync-AfterCmds*) (RevSync:run))
)

(defun RevSync:reactorOff ()
  (if *RevSync-Reactor* (vlr-remove *RevSync-Reactor*))
  (setq *RevSync-Reactor* nil)
)

(defun RevSync:reactorOn ()
  (RevSync:reactorOff)
  (setq *RevSync-Reactor*
    (vlr-command-reactor "RevSync"
      '((:vlr-commandWillStart . RevSync:onWillStart)
        (:vlr-commandEnded     . RevSync:onEnded))
    )
  )
)

;;; ---------------------------------------------------------------------------
;;; Commands

(defun c:REVSYNCSETUP (/ sel ent ed blk)
  (setq sel (nentsel "\nSelect the revision number text inside a revision TRIANGLE: "))
  (if (and sel (= (cdr (assoc 0 (setq ed (entget (setq ent (car sel)))))) "ATTRIB"))
    (progn
      (setq blk (vlax-ename->vla-object (cdr (assoc 330 ed))))
      (setenv "RevSync_TriBlock" (RevSync:effName blk))
      (setenv "RevSync_TriTag" (cdr (assoc 2 ed)))
      (princ (strcat "\nTriangle block: " (RevSync:effName blk) "   attribute: " (cdr (assoc 2 ed))))
      (setq sel (nentsel "\nSelect the revision attribute in the TITLE BLOCK (Enter to keep current): "))
      (if (and sel (= (cdr (assoc 0 (setq ed (entget (car sel))))) "ATTRIB"))
        (setenv "RevSync_TitleTag" (cdr (assoc 2 ed)))
      )
      (princ (strcat "\nTitle block attribute: " (RevSync:titleTag)))
      (c:REVSYNC)
    )
    (princ "\nThat isn't an attribute. Pick directly on the rev number text.")
  )
  (princ)
)

(defun c:REVSYNC (/ res)
  (if (= (RevSync:triBlock) "")
    (princ "\nRun REVSYNCSETUP first to tell RevSync which block is the revision triangle.")
    (progn
      (setq res (RevSync:sync (vla-get-ActiveDocument (vlax-get-acad-object))))
      (princ (strcat "\nRevSync: " (itoa (car res)) " triangle(s) updated."))
      (if (cdr res) (princ (strcat "\n" (cdr res))))
    )
  )
  (princ)
)

(defun c:REVSYNCON ()
  (RevSync:reactorOn)
  (princ "\nRevSync automatic syncing ON.")
  (princ)
)

(defun c:REVSYNCOFF ()
  (RevSync:reactorOff)
  (princ "\nRevSync automatic syncing OFF.")
  (princ)
)

(RevSync:reactorOn)
(princ "\nRevSync loaded. Commands: REVSYNCSETUP, REVSYNC, REVSYNCON, REVSYNCOFF")
(princ)
